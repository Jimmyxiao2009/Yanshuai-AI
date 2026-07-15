// TestQwen — P2 单层对齐：C# 引擎各层 vs HF 参考 逐阶段比对
using System;
using System.Collections.Generic;
using System.IO;
using Yanshuai.Qwen;

class Program
{
    static string RefDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "model_export"));

    static int Main(string[] args)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
        bool all = true;
        all &= TestDeltaNet(Path.Combine(RefDir, "reference_deltanet_layer0.bin"));
        Console.WriteLine();
        all &= TestAttention(Path.Combine(RefDir, "reference_attn_layer3.bin"));
        Console.WriteLine();
        all &= TestMlp(Path.Combine(RefDir, "reference_mlp_layer0.bin"));
        Console.WriteLine();
        all &= TestFullNet(Path.Combine(RefDir, "qwen3_5_0_8b-int8.llmmodel"),
                           Path.Combine(RefDir, "reference_fullnet.bin"));
        Console.WriteLine();
        all &= TestTokenizer(Path.Combine(RefDir, "reference_tokenizer.json"));
        Console.WriteLine();
        Console.WriteLine(all ? "🎉 全部对齐通过" : "❌ 有项未通过");
        return all ? 0 : 1;
    }

    // ── P2a: Gated DeltaNet ────────────────────────────────────────────────
    static bool TestDeltaNet(string bin)
    {
        Console.WriteLine("=== P2a · Gated DeltaNet 单层 ===");
        if (!File.Exists(bin)) { Console.WriteLine("  跳过（缺 " + Path.GetFileName(bin) + "）"); return true; }
        var t = RefTensorFile.Load(bin);

        var qkv = t["w.in_proj_qkv.weight"];
        int hidden = qkv.Shape[1];
        var normW = t["w.norm.weight"]; int vdim = normW.Shape[0];
        var wout = t["w.out_proj.weight"]; int nvh = wout.Shape[1] / vdim;
        var conv = t["w.conv1d.weight"]; int convK = conv.Shape[conv.Shape.Length - 1];

        var dims = new DeltaNetDims
        {
            Hidden = hidden, NumKHeads = nvh, NumVHeads = nvh,
            KHeadDim = vdim, VHeadDim = vdim, ConvK = convK, Eps = 1e-6f
        };
        var inp = t["input"]; int S = inp.Shape[0];
        var net = new DeltaNet(dims,
            Mat(t["w.in_proj_qkv.weight"]), Mat(t["w.in_proj_a.weight"]), Mat(t["w.in_proj_b.weight"]),
            Mat(t["w.in_proj_z.weight"]), conv.Data, t["w.A_log"].Data, t["w.dt_bias"].Data, normW.Data,
            Mat(t["w.out_proj.weight"]));
        var dbg = new DeltaNetDebug();
        var outp = net.Forward(inp.Data, S, dbg);

        bool ok = true;
        ok &= Cmp("mixed_conv", dbg.MixedConv, t["mid.mixed_conv"]);
        ok &= Cmp("g", dbg.G, t["mid.g"]);
        ok &= Cmp("beta", dbg.Beta, t["mid.beta"]);
        ok &= Cmp("core", dbg.Core, t["mid.core"]);
        ok &= Cmp("normed", dbg.Normed, t["mid.normed"]);
        ok &= Cmp("OUTPUT vs HF", outp, t["ref_output"], 1e-3f);
        Console.WriteLine("  " + (ok ? "✅ 通过" : "❌ 未通过"));
        return ok;
    }

    // ── P2b: Full Attention ────────────────────────────────────────────────
    static bool TestAttention(string bin)
    {
        Console.WriteLine("=== P2b · Full Attention 单层 ===");
        if (!File.Exists(bin)) { Console.WriteLine("  跳过（缺 " + Path.GetFileName(bin) + "）"); return true; }
        var t = RefTensorFile.Load(bin);

        var wq = t["w.q_proj.weight"];    // [4096,1024]
        int hidden = wq.Shape[1];
        var wk = t["w.k_proj.weight"];    // [512,1024]
        var qn = t["w.q_norm.weight"];    // [256]
        int hd = qn.Shape[0];
        int nh = wq.Shape[0] / (2 * hd);  // 4096/512 = 8
        int nkv = wk.Shape[0] / hd;       // 512/256 = 2
        var cos = t["cos"]; int rot = cos.Shape[cos.Shape.Length - 1];

        var dims = new FullAttentionDims
        {
            Hidden = hidden, NumHeads = nh, NumKVHeads = nkv, HeadDim = hd,
            RotaryDim = rot, RopeTheta = 1e7f, Eps = 1e-6f
        };
        var inp = t["input"]; int S = inp.Shape[0];
        var att = new FullAttention(dims, Mat(wq), Mat(wk), Mat(t["w.v_proj.weight"]),
            Mat(t["w.o_proj.weight"]), qn.Data, t["w.k_norm.weight"].Data);
        var dbg = new FullAttentionDebug();
        var outp = att.Forward(inp.Data, S, dbg);

        bool ok = true;
        ok &= Cmp("cos (RoPE表)", dbg.Cos, t["cos"]);
        ok &= Cmp("sin (RoPE表)", dbg.Sin, t["sin"]);
        ok &= Cmp("q_normed", dbg.QNormed, t["mid.q_normed"]);
        ok &= Cmp("q_roped", dbg.QRoped, t["mid.q_roped"]);
        ok &= Cmp("attn_pre_gate", dbg.AttnPreGate, t["mid.attn_pre_gate"]);
        ok &= Cmp("OUTPUT vs HF", outp, t["ref_output"], 1e-3f);
        Console.WriteLine("  " + (ok ? "✅ 通过" : "❌ 未通过"));
        return ok;
    }

    // ── SwiGLU MLP ─────────────────────────────────────────────────────────
    static bool TestMlp(string bin)
    {
        Console.WriteLine("=== P2 · SwiGLU MLP 单层 ===");
        if (!File.Exists(bin)) { Console.WriteLine("  跳过（缺 " + Path.GetFileName(bin) + "）"); return true; }
        var t = RefTensorFile.Load(bin);
        var gate = t["w.gate_proj.weight"];           // [inter,hidden]
        int inter = gate.Shape[0], hidden = gate.Shape[1];
        var inp = t["input"]; int S = inp.Shape[0];
        var mlp = new SwiGluMlp(hidden, inter, Mat(gate), Mat(t["w.up_proj.weight"]), Mat(t["w.down_proj.weight"]));
        var outp = mlp.Forward(inp.Data, S);
        bool ok = Cmp("OUTPUT vs HF", outp, t["ref_output"], 1e-3f);
        Console.WriteLine("  " + (ok ? "✅ 通过" : "❌ 未通过"));
        return ok;
    }

    // ── P2c: 整网（int8 模型 vs numpy int8 仿真 + HF fp32 真值）────────────
    static bool TestFullNet(string modelPath, string refPath)
    {
        Console.WriteLine("=== P2c · 整网 int8 ===");
        if (!File.Exists(modelPath)) { Console.WriteLine("  跳过（缺 " + Path.GetFileName(modelPath) + "）"); return true; }
        if (!File.Exists(refPath)) { Console.WriteLine("  跳过（缺 " + Path.GetFileName(refPath) + "）"); return true; }

        var t = RefTensorFile.Load(refPath);
        var idsF = t["token_ids"];
        var ids = new int[idsF.Count];
        for (int i = 0; i < ids.Length; i++) ids[i] = (int)idsF.Data[i];

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var model = QwenModel.Load(modelPath);
        _model = model;
        Console.WriteLine($"  模型加载 {sw.Elapsed.TotalSeconds:0.0}s  " +
                          $"({model.NumLayers} 层, vocab {model.VocabSize}, hidden {model.Hidden})");

        var runner = new QwenRunner(model);
        runner.Reset();

        sw.Restart();
        var hiddens = new List<float[]>();
        float[] logits = runner.Forward(ids, hiddens);
        Console.WriteLine($"  prefill {ids.Length} tokens: {sw.Elapsed.TotalSeconds:0.0}s");

        // 与 numpy int8 仿真逐层对齐（相对误差）
        bool ok = true;
        ok &= CmpRel("embed(h0)", hiddens[0], t["npq.h0"], 1e-4f);
        for (int i = 1; i <= model.NumLayers; i++)
        {
            string key = "npq.h" + i;
            if (!t.ContainsKey(key)) break;
            ok &= CmpRel("h" + i, hiddens[i], t[key], 2e-3f);
        }
        ok &= CmpRel("logits", logits, t["npq.logits_last"], 2e-3f);

        int argmax = QwenRunner.ArgMax(logits);
        int npqArg = QwenRunner.ArgMax(t["npq.logits_last"].Data);
        Console.WriteLine($"  argmax: C#={argmax}  npq={npqArg}  {(argmax == npqArg ? "✅ 一致" : "❌ 不一致")}");
        ok &= argmax == npqArg;

        if (t.ContainsKey("hf.logits_last"))
        {
            var hf = t["hf.logits_last"].Data;
            double dot = 0, na = 0, nb = 0;
            for (int i = 0; i < hf.Length; i++)
            {
                dot += (double)logits[i] * hf[i];
                na += (double)logits[i] * logits[i];
                nb += (double)hf[i] * hf[i];
            }
            double cos = dot / (Math.Sqrt(na) * Math.Sqrt(nb));
            int hfArg = QwenRunner.ArgMax(hf);
            Console.WriteLine($"  vs HF fp32: logits 余弦={cos:0.000000}  argmax {(argmax == hfArg ? "一致" : "不一致")}");
        }

        // 贪心续写（增量解码路径：KV cache + DeltaNet 递归 + 卷积缓冲）
        sw.Restart();
        var gen = new List<int>();
        int next = argmax;
        for (int step = 0; step < 24; step++)
        {
            gen.Add(next);
            if (next == model.EosId || next == model.EosId2) break;
            logits = runner.Forward(new[] { next });
            next = QwenRunner.ArgMax(logits);
        }
        Console.WriteLine($"  贪心 {gen.Count} tokens: {sw.Elapsed.TotalSeconds:0.0}s " +
                          $"({gen.Count / Math.Max(0.001, sw.Elapsed.TotalSeconds):0.00} tok/s)");

        var dec = new BpeDecoder(model.Vocab);
        Console.WriteLine($"  提示: {dec.Decode(ids)}");
        Console.WriteLine($"  续写: {dec.Decode(gen)}");

        if (t.ContainsKey("hf.greedy_ids"))
        {
            var hfG = t["hf.greedy_ids"].Data;
            int match = 0;
            while (match < gen.Count && match < hfG.Length && gen[match] == (int)hfG[match]) match++;
            Console.WriteLine($"  vs HF 贪心: 前 {match}/{Math.Min(gen.Count, hfG.Length)} 个 token 一致" +
                              (match < Math.Min(gen.Count, hfG.Length) ? "（int8 量化后期分叉属正常）" : " ✅"));
            ok &= match >= 3;   // 至少前几个 token 必须与 fp32 真值一致
        }

        Console.WriteLine("  " + (ok ? "✅ 通过" : "❌ 未通过"));
        return ok;
    }

    static MatW Mat(RefTensor t) => MatW.FromF32(t.Data, t.Shape[0], t.Shape[t.Shape.Length - 1]);

    static QwenModel _model;

    // ── P3: byte-level BPE 编码 vs HF ─────────────────────────────────────
    static bool TestTokenizer(string jsonPath)
    {
        Console.WriteLine("=== P3 · BPE 编码 ===");
        if (_model == null) { Console.WriteLine("  跳过（模型未加载）"); return true; }
        if (!File.Exists(jsonPath)) { Console.WriteLine("  跳过（缺 " + Path.GetFileName(jsonPath) + "）"); return true; }

        var tok = new BpeTokenizer(_model.Vocab, _model.Merges, _model.EosId);
        var dec = new BpeDecoder(_model.Vocab);

        bool ok = true;
        using (var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(jsonPath)))
        {
            foreach (string group in new[] { "sentences", "chat_segments" })
            {
                foreach (var item in doc.RootElement.GetProperty(group).EnumerateArray())
                {
                    string text = item.GetProperty("text").GetString();
                    var expIds = new List<int>();
                    foreach (var v in item.GetProperty("ids").EnumerateArray()) expIds.Add(v.GetInt32());

                    List<int> got = tok.Encode(text);
                    bool same = got.Count == expIds.Count;
                    if (same)
                        for (int i = 0; i < got.Count; i++)
                            if (got[i] != expIds[i]) { same = false; break; }

                    string label = text.Length > 24 ? text.Substring(0, 24) + "…" : text;
                    label = label.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
                    Console.WriteLine($"  {(same ? "✅" : "❌")} [{got.Count}/{expIds.Count}] {label}");
                    if (!same)
                    {
                        Console.WriteLine($"      got: {string.Join(",", got)}");
                        Console.WriteLine($"      exp: {string.Join(",", expIds)}");
                    }
                    ok &= same;

                    // 解码回环
                    string round = dec.Decode(got);
                    if (round != text)
                    {
                        Console.WriteLine($"  ⚠️ 解码回环不一致: {round.Replace("\n", "\\n")}");
                    }
                }
            }
        }
        Console.WriteLine("  " + (ok ? "✅ 通过" : "❌ 未通过"));
        return ok;
    }

    /// <summary>相对误差判定：max|Δ| / max|ref| ≤ tol。</summary>
    static bool CmpRel(string label, float[] got, RefTensor exp, float tol)
    {
        if (got == null || got.Length != exp.Count)
        {
            Console.WriteLine($"  ❌ {label}: 长度不符 got={(got == null ? -1 : got.Length)} exp={exp.Count}");
            return false;
        }
        double maxAbs = 0, refMax = 0; int argmax = -1;
        for (int i = 0; i < got.Length; i++)
        {
            double da = Math.Abs((double)got[i] - exp.Data[i]);
            if (da > maxAbs) { maxAbs = da; argmax = i; }
            double r = Math.Abs(exp.Data[i]); if (r > refMax) refMax = r;
        }
        double maxRel = maxAbs / (refMax + 1e-9);
        bool pass = maxRel <= tol;
        Console.WriteLine($"  {(pass ? "✅" : "❌")} {label,-12} max|Δ|={maxAbs:0.000e+00} rel={maxRel:0.000e+00}"
            + (pass ? "" : $"  @i={argmax} got={got[argmax]:0.0000} exp={exp.Data[argmax]:0.0000}"));
        return pass;
    }

    static bool Cmp(string label, float[] got, RefTensor exp, float tol = 5e-4f)
    {
        if (got == null) { Console.WriteLine($"  [skip] {label} (null)"); return false; }
        if (got.Length != exp.Count)
        {
            Console.WriteLine($"  ❌ {label}: 长度不符 got={got.Length} exp={exp.Count}");
            return false;
        }
        double maxAbs = 0, refMax = 0; int argmax = -1;
        for (int i = 0; i < got.Length; i++)
        {
            double da = Math.Abs((double)got[i] - exp.Data[i]);
            if (da > maxAbs) { maxAbs = da; argmax = i; }
            double r = Math.Abs(exp.Data[i]); if (r > refMax) refMax = r;
        }
        double maxRel = maxAbs / (refMax + 1e-9);
        bool pass = maxAbs <= tol;
        Console.WriteLine($"  {(pass ? "✅" : "❌")} {label,-22} max|Δ|={maxAbs:0.000e+00} rel={maxRel:0.000e+00}"
            + (pass ? "" : $"  @i={argmax} got={got[argmax]:0.0000} exp={exp.Data[argmax]:0.0000}"));
        return pass;
    }
}
