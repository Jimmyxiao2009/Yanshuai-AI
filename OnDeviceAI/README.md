# OnDeviceAI — W10M 本地嵌入模型推理引擎

在 Windows 10 Mobile 上通过 Direct3D 11 Compute Shader 运行嵌入模型，
为言枢智能体提供 **完全离线** 的语义 RAG 检索。

## 架构

```
用户输入 (C#)
    │
    ▼
OnEmbedder.dll (C++/CX WinRT 组件)
    │
    ├── WordPiece Tokenizer (CPU)
    │   └── 输入文本 → token_ids[seq_len]
    │
    ├── D3D11 Compute Shader Pipeline (GPU)
    │   │
    │   ├─ embedding_lookup.cs.hlsl
    │   │   → Token Embedding + Position Embedding + LayerNorm
    │   │
    │   ├─ transformer_block.cs.hlsl (重复 num_layers 次)
    │   │   → MHA + FFN + Residual + LayerNorm
    │   │
    │   └─ pooler.cs.hlsl
    │       → Mean Pooling + L2 Normalize
    │
    ▼
float[384] — 语义向量
    │
    ├→ MemoryStore.Search() — 向量搜索
    └→ 纯 math，全离线，<500ms
```

## 目录结构

```
OnDeviceAI/
├── model_export/            ← Python：导出模型
│   ├── export_model.py       将 sentence-transformers → .embmodel
│   ├── embmodel_format.md    二进制格式规范
│   └── requirements.txt      Python 依赖
│
├── shaders/                 ← HLSL Compute Shader
│   ├── embedding_lookup.cs.hlsl   嵌入查找 + LayerNorm
│   ├── transformer_block.cs.hlsl  注意力 + FFN
│   └── pooler.cs.hlsl             均值池化 + L2归一化
│
└── OnEmbedder/             ← C++/CX WinRT 组件 (VS2015 UWP)
    ├── OnEmbedder.h          头文件
    ├── OnEmbedder.cpp        实现
    └── OnEmbedder.vcxproj    项目文件 (待生成)
```

## 使用流程

### 1. 导出模型 (在 PC 上)

```bash
cd model_export
pip install -r requirements.txt
python export_model.py --model BAAI/bge-small-zh-v1.5 --output bge.embmodel
```

### 2. 将 .embmodel 作为 Assets 添加到 UWP 项目

### 3. 在 C# 中调用

```csharp
var embedder = new OnEmbedder.EmbeddingEngine();
embedder.Initialize();
embedder.LoadModel("ms-appx:///Assets/bge.embmodel");

float[] vector = embedder.Encode("用户输入的问题");
var results = MemoryStore.Search(vector, topK: 5);
```

## 性能预估 (骁龙 800/801，Adreno 330)

| 模型 | 参数 | 单次推理 | 备注 |
|------|:----:|:--------:|------|
| MiniLM-L6 | 22M | 100-200ms | 384维，最轻量 |
| bge-small-zh | 33M | 150-300ms | 384维，中文优化 |
| bge-base-zh | 102M | 400-800ms | 768维，效果好但重 |

## 依赖

- Visual Studio 2015 或更高 (用于编译 C++/CX 组件)
- Windows 10 SDK 10.0.10240.0 (W10M 兼容)
- Direct3D 11.1 (所有 W10M 设备均支持)
- Python 3.8+ (仅导出阶段)
