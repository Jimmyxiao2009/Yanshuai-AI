# OnDeviceAI2 — Windows 10 Mobile 端侧通用 LLM 推理引擎

完全离线、跑在真实 Lumia 手机（Windows 10 Mobile 15254）上的大模型推理引擎。
起点是一个只能算嵌入向量的 BERT 引擎，现在能跑通 **Qwen3.5-0.8B**
（Qwen3-Next 混合线性/全注意力架构）的完整自回归对话。目标是做成
"能跑任意模型"的通用端侧运行时，而不是绑死某一个模型。

## 现状

| 引擎 | 状态 | 说明 |
|---|---|---|
| **Qwen（`Qwen/`）** | ✅ 能跑通完整对话 | 纯 C#，int8 权重常驻，24 层混合架构（18 Gated DeltaNet + 6 全注意力），贪心解码逐 token 与 HF fp32 对齐 |
| **OnEmbedder（`OnEmbedder/` + `shaders/`）** | 早期原型 | BERT 嵌入 + D3D11 Compute Shader，为言枢的离线语义检索设计；自注意力当时未真正实现 |

Qwen 引擎已验证：24 层整网 hidden state 与 numpy-int8 仿真逐层对齐（相对误差 ~1e-6）、
最终 logits 与 HF fp32 余弦相似度 0.9997、贪心续写 24/24 token 与 HF 完全一致。
`app/YanshuaiChat` 是跑在手机上的聊天应用外壳。

## 目录结构

```
OnDeviceAI2/
├── Qwen/                     ← Qwen3.5 推理引擎（纯 C#，UWP/.NET Native 可编译）
│   ├── QwenModel.cs           .llmmodel(v2) 加载器，int8 权重常驻
│   ├── QwenRunner.cs          整网前向（embed → 24 层 → final norm → tie lm_head）
│   ├── DeltaNet.cs            Gated DeltaNet 线性注意力层（18 层，递归状态）
│   ├── FullAttention.cs       GQA + partial RoPE + 输出门全注意力层（6 层，KV cache）
│   ├── SwiGluMlp.cs           SwiGLU 前馈层
│   ├── MatW.cs / QwenMath.cs  权重/数学基础算子（int8 常驻矩阵、MatVec、RmsNorm…）
│   ├── BpeTokenizer.cs        byte-level BPE 编码（文本 → id，含特殊 token 原子匹配）
│   └── BpeDecoder.cs          byte-level BPE 解码（id → 文本）
│
├── test_qwen/                 ← PC 端对齐/回归测试（`dotnet run -c Release`）
│
├── model_export/              ← Python：模型导出与逐层参考基准
│   ├── export_qwen.py          HF safetensors → .llmmodel(v2)，fp32/int8 可选
│   ├── dump_reference.py       P2a DeltaNet 单层参考（HF vs numpy 递归实现）
│   ├── dump_attn_reference.py  P2b 全注意力单层参考
│   ├── dump_mlp_reference.py   SwiGLU 单层参考
│   ├── dump_fullnet_reference.py   P2c 整网参考（HF fp32 真值 + numpy int8 仿真）
│   ├── dump_tokenizer_reference.py P3 tokenizer 参考
│   └── qwen3_5_0_8b/            HF 权重目录（config/tokenizer 已提交；.safetensors 见下）
│
├── app/YanshuaiChat/          ← UWP 聊天应用（ARM，Windows 10 Mobile 15063+）
│
├── docs/
│   ├── qwen-port-design.md            改造设计文档 + 分阶段里程碑
│   └── qwen3_5-0_8b-arch-reference.md 架构权威参考（张量名/形状/前向公式/易漏点）
│
├── OnEmbedder/ + shaders/     ← 早期 BERT 嵌入引擎（C++/CX + HLSL，见上）
└── test/                      ← OnEmbedder 的 PC 端测试
```

## 不进仓库的东西

`.gitignore` 排除了模型权重类大文件（`*.safetensors` `*.llmmodel` `*.onnx` `*.embmodel`）
和本地自签测试证书（`*.pfx`）。要跑起来需要自己：

```bash
cd model_export
pip install -r requirements.txt   # 需要较新 transformers（含 Qwen3_5 支持）
python export_qwen.py --model-dir qwen3_5_0_8b --output qwen3_5_0_8b-int8.llmmodel --quant int8
```

生成的 `.llmmodel`（int8 约 730MB）不要打进 appx 包，通过 YanshuaiChat 的
"导入模型"功能从手机本地存储（存储卡/Downloads）拷进应用的 LocalState。

## 跑测试

```bash
cd test_qwen
dotnet run -c Release
```

依次验证 DeltaNet 单层 / 全注意力单层 / SwiGLU 单层 / 整网 / tokenizer，
全部通过会打印 `🎉 全部对齐通过`。

## 已知瓶颈与优化方向

单 token 解码是内存带宽瓶颈（int8 权重只用一次就丢），不是纯算力瓶颈。
真机（骁龙 800/801，Krait 400，ARMv7，无 SIMD 加速）上首 token（prefill）
和生成速度都还慢。按预期收益排序的优化路径见 `docs/qwen-port-design.md`：

1. 移除非必要的 double 精度（`MatVec`/`DeltaNet` 状态本应是 fp32，用 double 只是早期
   对齐 numpy 参考的临时选择）——同时也是未来上 NEON 的前提（ARMv7 NEON 不支持双精度 SIMD）
2. 复用/预分配热路径缓冲区，减少解码过程中的 GC 压力
3. int4 分组量化（内存带宽减半，最大单项收益，但精度需重新验证）
4. 原生 NEON 组件（C++/WinRT，仿 `OnEmbedder.cpp` 的模式）
5. 重启 HLSL/D3D11 compute shader 路线（本仓库最初的设计意图）

## 许可

Qwen3.5 权重与架构来自阿里通义千问团队，Apache-2.0。本仓库的推理引擎代码
（`Qwen/`）与导出脚本（`model_export/*.py`）均为独立实现，未包含模型权重。
