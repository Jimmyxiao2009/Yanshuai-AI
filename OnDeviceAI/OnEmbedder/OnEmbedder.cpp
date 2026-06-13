// OnEmbedder.cpp - DirectCompute 嵌入推理引擎 DLL
// C# P/Invoke 接口（flat C exports，无 WinRT 依赖）

#include <windows.h>
#include <d3d11_1.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <vector>
#include <string>
#include <fstream>
#include <unordered_map>
#include <cstring>

using namespace Microsoft::WRL;

// ══════════════════════════════════════════════════════════════════
// .embmodel 格式结构
// ══════════════════════════════════════════════════════════════════

#pragma pack(push, 1)
struct ModelHeader
{
    uint32_t magic;           // 0x454D4245
    uint32_t version;
    uint32_t hidden_dim;
    uint32_t num_layers;
    uint32_t num_heads;
    uint32_t vocab_size;
    uint32_t max_seq_len;
    uint32_t pooler_dim;
    uint32_t ffn_dim;
    uint32_t tokenizer_type;
    uint32_t need_merge;
    uint32_t activation;
    uint32_t model_type;
    uint32_t embedding_type;
    uint32_t head_size;
    uint32_t reserved;
};
#pragma pack(pop)

// ══════════════════════════════════════════════════════════════════
// 内部实现类
// ══════════════════════════════════════════════════════════════════

class EmbeddingEngineImpl
{
public:
    EmbeddingEngineImpl() {}
    ~EmbeddingEngineImpl() { Cleanup(); }

    bool Initialize();
    bool LoadModel(const wchar_t* model_path);
    float* Encode(const char* text, int* out_dim);

private:
    // D3D11
    ComPtr<ID3D11Device>        m_device;
    ComPtr<ID3D11DeviceContext> m_context;

    // 模型数据
    ModelHeader m_header = {};
    std::vector<uint8_t> m_model_data;
    std::vector<std::pair<std::string, int>> m_vocab;

    // GPU 缓冲区
    ComPtr<ID3D11Buffer> m_embed_table;
    ComPtr<ID3D11Buffer> m_pos_table;
    ComPtr<ID3D11Buffer> m_ln_weight;
    ComPtr<ID3D11Buffer> m_ln_bias;

    struct LayerBuf
    {
        ComPtr<ID3D11Buffer> attn_ln_w, attn_ln_b;
        ComPtr<ID3D11Buffer> qkv_w, qkv_b;
        ComPtr<ID3D11Buffer> out_w, out_b;
        ComPtr<ID3D11Buffer> ffn_ln_w, ffn_ln_b;
        ComPtr<ID3D11Buffer> ffn1_w, ffn1_b;
        ComPtr<ID3D11Buffer> ffn2_w, ffn2_b;
    };
    std::vector<LayerBuf> m_layers;
    ComPtr<ID3D11Buffer> m_pooler_w;
    ComPtr<ID3D11Buffer> m_pooler_b;

    ComPtr<ID3D11Buffer> m_input_buf;
    ComPtr<ID3D11Buffer> m_output_buf;
    ComPtr<ID3D11Buffer> m_result_buf;

    // Shaders
    ComPtr<ID3D11ComputeShader> m_shader_embed;
    ComPtr<ID3D11ComputeShader> m_shader_block;
    ComPtr<ID3D11ComputeShader> m_shader_pool;

    // 临时结果（由 Encode 返回，后续调用覆盖）
    std::vector<float> m_last_result;

    // Tokenizer
    std::unordered_map<std::string, int> m_vocab_map;

    bool CreateBuffer(size_t size, ComPtr<ID3D11Buffer>& buf, D3D11_BIND_FLAG bind, const void* data);
    bool LoadShaderFromCSO(const void* data, size_t size, ComPtr<ID3D11ComputeShader>& shader);
    void RunShader(ID3D11ComputeShader* shader, uint32_t x, uint32_t y, uint32_t z);
    void Cleanup();
    std::vector<uint32_t> Tokenize(const std::string& text);
};

// ══════════════════════════════════════════════════════════════════
// Implementation
// ══════════════════════════════════════════════════════════════════

bool EmbeddingEngineImpl::Initialize()
{
    UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
#ifdef _DEBUG
    flags |= D3D11_CREATE_DEVICE_DEBUG;
#endif
    D3D_FEATURE_LEVEL levels[] = { D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0 };

    HRESULT hr = D3D11CreateDevice(
        nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, flags,
        levels, ARRAYSIZE(levels), D3D11_SDK_VERSION,
        &m_device, nullptr, &m_context);

    return SUCCEEDED(hr);
}

bool EmbeddingEngineImpl::LoadShaderFromCSO(const void* data, size_t size, ComPtr<ID3D11ComputeShader>& shader)
{
    HRESULT hr = m_device->CreateComputeShader(data, size, nullptr, &shader);
    return SUCCEEDED(hr);
}

bool EmbeddingEngineImpl::CreateBuffer(size_t size, ComPtr<ID3D11Buffer>& buf, D3D11_BIND_FLAG bind, const void* data)
{
    D3D11_BUFFER_DESC desc = {};
    desc.ByteWidth = (UINT)size;
    desc.BindFlags = bind;
    desc.Usage = D3D11_USAGE_DEFAULT;
    desc.StructureByteStride = 0;
    if (bind == D3D11_BIND_UNORDERED_ACCESS)
        desc.MiscFlags = D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;
    if (bind == D3D11_BIND_CONSTANT_BUFFER)
        desc.ByteWidth = (UINT)((size + 15) & ~15); // 16-byte align

    D3D11_SUBRESOURCE_DATA init = {};
    init.pSysMem = data;

    HRESULT hr = m_device->CreateBuffer(&desc, data ? &init : nullptr, &buf);
    return SUCCEEDED(hr);
}

void EmbeddingEngineImpl::RunShader(ID3D11ComputeShader* shader, uint32_t x, uint32_t y, uint32_t z)
{
    m_context->CSSetShader(shader, nullptr, 0);
    m_context->Dispatch(x, y, z);
}

void EmbeddingEngineImpl::Cleanup()
{
    m_shader_embed.Reset();
    m_shader_block.Reset();
    m_shader_pool.Reset();
    m_embed_table.Reset();
    m_pos_table.Reset();
    m_ln_weight.Reset();
    m_ln_bias.Reset();
    m_layers.clear();
    m_pooler_w.Reset();
    m_pooler_b.Reset();
    m_input_buf.Reset();
    m_output_buf.Reset();
    m_result_buf.Reset();
    m_context.Reset();
    m_device.Reset();
}

// ══════════════════════════════════════════════════════════════════
// 读取 .embmodel
// ══════════════════════════════════════════════════════════════════

static std::vector<uint8_t> ReadFile(const wchar_t* path)
{
    std::ifstream file(path, std::ios::binary | std::ios::ate);
    if (!file) return {};
    size_t size = (size_t)file.tellg();
    file.seekg(0);
    std::vector<uint8_t> data(size);
    file.read((char*)data.data(), size);
    return data;
}

static const uint8_t* ReadTensor(const uint8_t* ptr, const uint8_t* end, std::vector<float>& out)
{
    if (ptr + 4 > end) return nullptr;
    uint32_t ndim;
    memcpy(&ndim, ptr, 4); ptr += 4;

    std::vector<uint32_t> dims(ndim);
    for (uint32_t i = 0; i < ndim; i++)
    {
        if (ptr + 4 > end) return nullptr;
        memcpy(&dims[i], ptr, 4); ptr += 4;
    }

    // Skip strides
    for (uint32_t i = 0; i < ndim; i++)
    {
        if (ptr + 4 > end) return nullptr;
        ptr += 4;
    }

    size_t total = 1;
    for (uint32_t d : dims) total *= d;

    if (ptr + total * 4 > end) return nullptr;
    out.resize(total);
    memcpy(out.data(), ptr, total * 4);
    ptr += total * 4;

    return ptr;
}

bool EmbeddingEngineImpl::LoadModel(const wchar_t* model_path)
{
    m_model_data = ReadFile(model_path);
    if (m_model_data.empty()) return false;

    const uint8_t* ptr = m_model_data.data();
    const uint8_t* end = ptr + m_model_data.size();

    // Header
    if (ptr + sizeof(ModelHeader) > end) return false;
    memcpy(&m_header, ptr, sizeof(ModelHeader));
    ptr += sizeof(ModelHeader);

    if (m_header.magic != 0x454D4245) return false;

    // Tokenizer vocab
    if (ptr + 4 > end) return false;
    uint32_t vocab_size;
    memcpy(&vocab_size, ptr, 4); ptr += 4;

    m_vocab.clear();
    m_vocab_map.clear();
    for (uint32_t i = 0; i < vocab_size; i++)
    {
        if (ptr + 4 > end) return false;
        uint32_t tlen;
        memcpy(&tlen, ptr, 4); ptr += 4;
        if (ptr + tlen + 4 > end) return false;
        std::string token((char*)ptr, tlen); ptr += tlen;
        uint32_t tid;
        memcpy(&tid, ptr, 4); ptr += 4;
        m_vocab.push_back({ token, (int)tid });
        m_vocab_map[token] = (int)tid;
        ptr += 4;
    }

    // Skip merges
    if (ptr + 4 > end) return false;
    uint32_t merges;
    memcpy(&merges, ptr, 4); ptr += 4;
    for (uint32_t i = 0; i < merges; i++)
    {
        if (ptr + 4 > end) return false;
        uint32_t mlen;
        memcpy(&mlen, ptr, 4); ptr += 4;
        ptr += mlen;
    }

    // Embedding weights
    std::vector<float> embed_w, pos_w, ln_w, ln_b;
    ptr = ReadTensor(ptr, end, embed_w);
    ptr = ReadTensor(ptr, end, pos_w);
    ptr = ReadTensor(ptr, end, ln_w);
    ptr = ReadTensor(ptr, end, ln_b);
    if (!ptr) return false;

    // Create GPU buffers for embeddings
    CreateBuffer(embed_w.size() * 4, m_embed_table, D3D11_BIND_SHADER_RESOURCE, embed_w.data());
    CreateBuffer(pos_w.size() * 4, m_pos_table, D3D11_BIND_SHADER_RESOURCE, pos_w.data());
    CreateBuffer(ln_w.size() * 4, m_ln_weight, D3D11_BIND_SHADER_RESOURCE, ln_w.data());
    CreateBuffer(ln_b.size() * 4, m_ln_bias, D3D11_BIND_SHADER_RESOURCE, ln_b.data());

    // Transformer layers
    m_layers.resize(m_header.num_layers);
    for (uint32_t l = 0; l < m_header.num_layers; l++)
    {
        auto& layer = m_layers[l];
        std::vector<float> tensors[12];

        for (int t = 0; t < 12; t++)
        {
            ptr = ReadTensor(ptr, end, tensors[t]);
            if (!ptr) return false;
        }

        CreateBuffer(tensors[0].size() * 4, layer.attn_ln_w, D3D11_BIND_SHADER_RESOURCE, tensors[0].data());
        CreateBuffer(tensors[1].size() * 4, layer.attn_ln_b, D3D11_BIND_SHADER_RESOURCE, tensors[1].data());
        CreateBuffer(tensors[2].size() * 4, layer.qkv_w, D3D11_BIND_SHADER_RESOURCE, tensors[2].data());
        CreateBuffer(tensors[3].size() * 4, layer.qkv_b, D3D11_BIND_SHADER_RESOURCE, tensors[3].data());
        CreateBuffer(tensors[4].size() * 4, layer.out_w, D3D11_BIND_SHADER_RESOURCE, tensors[4].data());
        CreateBuffer(tensors[5].size() * 4, layer.out_b, D3D11_BIND_SHADER_RESOURCE, tensors[5].data());
        CreateBuffer(tensors[6].size() * 4, layer.ffn_ln_w, D3D11_BIND_SHADER_RESOURCE, tensors[6].data());
        CreateBuffer(tensors[7].size() * 4, layer.ffn_ln_b, D3D11_BIND_SHADER_RESOURCE, tensors[7].data());
        CreateBuffer(tensors[8].size() * 4, layer.ffn1_w, D3D11_BIND_SHADER_RESOURCE, tensors[8].data());
        CreateBuffer(tensors[9].size() * 4, layer.ffn1_b, D3D11_BIND_SHADER_RESOURCE, tensors[9].data());
        CreateBuffer(tensors[10].size() * 4, layer.ffn2_w, D3D11_BIND_SHADER_RESOURCE, tensors[10].data());
        CreateBuffer(tensors[11].size() * 4, layer.ffn2_b, D3D11_BIND_SHADER_RESOURCE, tensors[11].data());
    }

    // Pooler
    if (ptr + 4 > end) return false;
    uint32_t has_pooler;
    memcpy(&has_pooler, ptr, 4); ptr += 4;

    if (has_pooler)
    {
        std::vector<float> pooler_w, pooler_b;
        ptr = ReadTensor(ptr, end, pooler_w);
        ptr = ReadTensor(ptr, end, pooler_b);
        if (!ptr) return false;
        CreateBuffer(pooler_w.size() * 4, m_pooler_w, D3D11_BIND_SHADER_RESOURCE, pooler_w.data());
        CreateBuffer(pooler_b.size() * 4, m_pooler_b, D3D11_BIND_SHADER_RESOURCE, pooler_b.data());
    }

    // 创建工作缓冲区
    uint32_t hidden_dim = m_header.hidden_dim;
    uint32_t max_seq = m_header.max_seq_len;
    CreateBuffer(max_seq * hidden_dim * 4, m_input_buf, D3D11_BIND_UNORDERED_ACCESS);
    CreateBuffer(max_seq * hidden_dim * 4, m_output_buf, D3D11_BIND_UNORDERED_ACCESS);
    CreateBuffer(hidden_dim * 4, m_result_buf, D3D11_BIND_UNORDERED_ACCESS);

    return true;
}

// ══════════════════════════════════════════════════════════════════
// WordPiece Tokenizer (简化版)
// ══════════════════════════════════════════════════════════════════

std::vector<uint32_t> EmbeddingEngineImpl::Tokenize(const std::string& text)
{
    std::vector<uint32_t> ids;
    ids.push_back(101); // [CLS]

    // 简单按空格分词 + WordPiece
    std::string word;
    for (char c : text)
    {
        if (c == ' ' || c == '\t' || c == '\n')
        {
            if (!word.empty())
            {
                // WordPiece: 尝试完整词，否则加 ## 前缀
                auto it = m_vocab_map.find(word);
                if (it != m_vocab_map.end())
                {
                    ids.push_back(it->second);
                }
                else
                {
                    // 按字切分作为 fallback
                    for (char ch : word)
                    {
                        std::string char_str(1, ch);
                        auto cit = m_vocab_map.find(char_str);
                        if (cit != m_vocab_map.end())
                            ids.push_back(cit->second);
                        else
                            ids.push_back(100); // [UNK]
                    }
                }
                word.clear();
            }
        }
        else
        {
            word += c;
        }
    }
    // 最后一个词
    if (!word.empty())
    {
        auto it = m_vocab_map.find(word);
        if (it != m_vocab_map.end())
            ids.push_back(it->second);
        else
            ids.push_back(100);
    }

    ids.push_back(102); // [SEP]

    // Truncate
    if ((int)ids.size() > m_header.max_seq_len)
        ids.resize(m_header.max_seq_len - 1);
    ids.back() = 102; // 确保末尾是 [SEP]

    return ids;
}

// ══════════════════════════════════════════════════════════════════
// 推理接口
// ══════════════════════════════════════════════════════════════════

float* EmbeddingEngineImpl::Encode(const char* text, int* out_dim)
{
    auto ids = Tokenize(text);
    uint32_t seq_len = (uint32_t)ids.size();

    // TODO: 完整的 GPU pipeline — HLSL shader 已就绪，
    // 此处先用简化方式：CPU 计算均值作为占位符，后续可加完整 GPU 流水线
    //
    // 完整 GPU pipeline 流程:
    // 1. embedding_lookup: token_ids → hidden states
    // 2. transformer_block × num_layers: attention + FFN
    // 3. pooler: mean pooling + L2 normalize
    //
    // 占位实现：直接返回随机向量用于测试
    uint32_t hidden_dim = m_header.hidden_dim;
    m_last_result.resize(hidden_dim);

    // 简易方式：对词表嵌入做平均作为占位 embedding
    // （正式版会用完整的 HLSL pipeline）
    auto model_data = m_model_data.data();
    const ModelHeader* hdr = (const ModelHeader*)model_data;
    const float* embed_table = nullptr;

    // 从模型数据中定位 token embedding
    const uint8_t* ptr = model_data + sizeof(ModelHeader);
    uint32_t vsize;
    memcpy(&vsize, ptr, 4); ptr += 4;
    for (uint32_t i = 0; i < vsize; i++)
    {
        uint32_t tlen;
        memcpy(&tlen, ptr, 4); ptr += 4;
        ptr += tlen + 4;
    }
    uint32_t merges;
    memcpy(&merges, ptr, 4); ptr += 4;
    for (uint32_t i = 0; i < merges; i++)
    {
        uint32_t mlen;
        memcpy(&mlen, ptr, 4); ptr += 4;
        ptr += mlen;
    }

    // embedding weights start here
    const float* tensors_start = (const float*)ptr;

    // For now, average the first `seq_len` token embeddings
    uint32_t vocab_count = hdr->vocab_size;
    uint32_t hdim = hdr->hidden_dim;
    embed_table = tensors_start; // [vocab_size, hidden_dim]

    // Average token embeddings
    std::fill(m_last_result.begin(), m_last_result.end(), 0.0f);
    for (uint32_t i = 0; i < seq_len && i < hdr->max_seq_len; i++)
    {
        uint32_t tid = ids[i];
        if (tid < vocab_count)
        {
            for (uint32_t j = 0; j < hdim; j++)
                m_last_result[j] += embed_table[tid * hdim + j];
        }
    }
    float scale = 1.0f / (float)seq_len;
    for (uint32_t j = 0; j < hdim; j++)
        m_last_result[j] *= scale;

    // L2 normalize
    float sum_sq = 0.0f;
    for (uint32_t j = 0; j < hdim; j++)
        sum_sq += m_last_result[j] * m_last_result[j];
    float inv_norm = 1.0f / (sqrtf(sum_sq) + 1e-12f);
    for (uint32_t j = 0; j < hdim; j++)
        m_last_result[j] *= inv_norm;

    *out_dim = (int)hdim;
    return m_last_result.data();
}

// ══════════════════════════════════════════════════════════════════
// C 接口（供 C# P/Invoke）
// ══════════════════════════════════════════════════════════════════

extern "C"
{
    __declspec(dllexport) void* __stdcall Embedder_Create()
    {
        return new EmbeddingEngineImpl();
    }

    __declspec(dllexport) int __stdcall Embedder_Initialize(void* handle)
    {
        if (!handle) return 0;
        return ((EmbeddingEngineImpl*)handle)->Initialize() ? 1 : 0;
    }

    __declspec(dllexport) int __stdcall Embedder_LoadModel(void* handle, const wchar_t* model_path)
    {
        if (!handle || !model_path) return 0;
        return ((EmbeddingEngineImpl*)handle)->LoadModel(model_path) ? 1 : 0;
    }

    __declspec(dllexport) float* __stdcall Embedder_Encode(void* handle, const char* text, int* out_dim)
    {
        if (!handle || !text || !out_dim) return nullptr;
        return ((EmbeddingEngineImpl*)handle)->Encode(text, out_dim);
    }

    __declspec(dllexport) void __stdcall Embedder_Destroy(void* handle)
    {
        if (handle)
        {
            delete (EmbeddingEngineImpl*)handle;
        }
    }
}
