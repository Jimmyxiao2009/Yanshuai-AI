// OnEmbedder.h - DirectCompute 嵌入推理引擎
// C++/CX WinRT 组件，供 UWP C# 调用

#pragma once
#include <windows.h>
#include <d3d11_1.h>
#include <dxgi1_3.h>
#include <wrl/client.h>
#include <vector>
#include <string>

using namespace Microsoft::WRL;

namespace OnEmbedder
{
    // ══════════════════════════════════════════════════════════════════
    // .embmodel 加载器
    // ══════════════════════════════════════════════════════════════════

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

    class ModelLoader
    {
    public:
        ModelLoader();
        ~ModelLoader();

        bool Load(const std::wstring& path);
        const ModelHeader& GetHeader() const { return m_header; }

        // 获取各张量的原始指针 + 大小
        const float* GetEmbeddingWeight() const;
        const float* GetPositionWeight() const;
        const float* GetLnWeight() const;
        const float* GetLnBias() const;
        const float* GetLayerWeight(int layer, const std::string& name) const;

        // Tokenizer 数据
        const std::vector<std::pair<std::string, int>>& GetVocab() const { return m_vocab; }

    private:
        ModelHeader m_header = {};
        std::vector<uint8_t> m_data;
        std::vector<std::pair<std::string, int>> m_vocab;
        std::vector<size_t> m_layer_offsets;  // 每层起始偏移
        size_t m_embed_offset = 0;
        size_t m_pos_offset = 0;
        size_t m_lnw_offset = 0;
        size_t m_lnb_offset = 0;
        bool ReadHeader(std::ifstream& file);
        bool ReadTokenizer(std::ifstream& file);
    };

    // ══════════════════════════════════════════════════════════════════
    // WordPiece Tokenizer
    // ══════════════════════════════════════════════════════════════════

    class Tokenizer
    {
    public:
        Tokenizer();
        ~Tokenizer();

        bool Load(const std::vector<std::pair<std::string, int>>& vocab, int max_len = 128);
        std::vector<uint32_t> Encode(const std::string& text);

    private:
        std::unordered_map<std::string, int> m_vocab_map;
        std::unordered_map<int, std::string> m_id_to_token;
        int m_max_len = 128;
        int m_cls_id = 101;  // [CLS]
        int m_sep_id = 102;  // [SEP]
        int m_unk_id = 100;  // [UNK]
        int m_mask_id = 103; // [MASK]

        std::vector<std::string> SplitWordpiece(const std::string& word);
    };

    // ══════════════════════════════════════════════════════════════════
    // D3D11 DirectCompute 引擎
    // ══════════════════════════════════════════════════════════════════

    class ComputeEngine
    {
    public:
        ComputeEngine();
        ~ComputeEngine();

        bool Initialize();
        bool LoadModel(const std::wstring& model_path);
        std::vector<float> Encode(const std::string& text);

    private:
        // D3D11 设备
        ComPtr<ID3D11Device>        m_device;
        ComPtr<ID3D11DeviceContext> m_context;

        // 模型数据
        ModelLoader m_loader;
        Tokenizer   m_tokenizer;

        // GPU 缓冲区
        ComPtr<ID3D11Buffer> m_embed_table;    // token embedding
        ComPtr<ID3D11Buffer> m_pos_table;      // position embedding
        ComPtr<ID3D11Buffer> m_ln_weight;      // embedding layernorm
        ComPtr<ID3D11Buffer> m_ln_bias;

        struct LayerBuffers
        {
            ComPtr<ID3D11Buffer> attn_ln_w, attn_ln_b;
            ComPtr<ID3D11Buffer> qkv_w, qkv_b;
            ComPtr<ID3D11Buffer> out_w, out_b;
            ComPtr<ID3D11Buffer> ffn_ln_w, ffn_ln_b;
            ComPtr<ID3D11Buffer> ffn1_w, ffn1_b;
            ComPtr<ID3D11Buffer> ffn2_w, ffn2_b;
        };
        std::vector<LayerBuffers> m_layers;
        ComPtr<ID3D11Buffer> m_pooler_w;
        ComPtr<ID3D11Buffer> m_pooler_b;

        // 工作缓冲区
        ComPtr<ID3D11Buffer> m_input_buf;      // 输入 hidden states
        ComPtr<ID3D11Buffer> m_output_buf;     // 输出 hidden states
        ComPtr<ID3D11Buffer> m_qkv_buf;        // QKV 中间结果
        ComPtr<ID3D11Buffer> m_ffn_out_buf;    // FFN 输出
        ComPtr<ID3D11Buffer> m_result_buf;     // 最终 embedding 向量

        // Shaders
        ComPtr<ID3D11ComputeShader> m_shader_embed;
        ComPtr<ID3D11ComputeShader> m_shader_block;
        ComPtr<ID3D11ComputeShader> m_shader_pool;

        bool LoadShader(const std::wstring& path, ComPtr<ID3D11ComputeShader>& shader);
        bool CreateBuffer(size_t size, ComPtr<ID3D11Buffer>& buffer,
                          D3D11_BIND_FLAG bind = D3D11_BIND_UNORDERED_ACCESS,
                          const void* data = nullptr);
        void RunShader(ID3D11ComputeShader* shader, uint x, uint y, uint z);
    };

    // ══════════════════════════════════════════════════════════════════
    // WinRT 公开接口（供 C# 调用）
    // ══════════════════════════════════════════════════════════════════

    public ref class EmbeddingEngine sealed
    {
    public:
        EmbeddingEngine();
        virtual ~EmbeddingEngine();

        bool Initialize();
        bool LoadModel(Platform::String^ modelPath);
        Platform::Array<float>^ Encode(Platform::String^ text);

    private:
        ComputeEngine* m_engine;
    };
}
