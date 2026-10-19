using System.Text;

namespace LlamaLauncher;

internal sealed record GgufInfo(string Architecture, string Name, string Quantization, ulong? Context, string Template)
{
    public bool Thinking => Template.Contains("enable_thinking", StringComparison.Ordinal);
    public bool Reasoning => Template.Contains("reasoning_effort", StringComparison.Ordinal);
    public static GgufInfo Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        if (reader.ReadUInt32() != 0x46554747 || reader.ReadUInt32() != 3)
            throw new InvalidDataException("仅支持 GGUF v3 元数据。");
        reader.ReadUInt64();
        ulong count = reader.ReadUInt64();
        var values = new Dictionary<string, object>();
        for (ulong i = 0; i < count; i++)
        {
            string key = ReadString(reader);
            bool keep = key is "general.architecture" or "general.name" or "general.file_type" or "tokenizer.chat_template" or "tokenizer.chat_template.default" || key.EndsWith(".context_length", StringComparison.Ordinal);
            object? value = ReadValue(reader, reader.ReadUInt32(), keep);
            if (value is not null) values[key] = value;
        }
        string architecture = values.GetValueOrDefault("general.architecture")?.ToString() ?? "未声明";
        var context = values.GetValueOrDefault(architecture + ".context_length");
        return new(architecture, values.GetValueOrDefault("general.name")?.ToString() ?? "未声明",
            values.TryGetValue("general.file_type", out var type) ? Quant(Convert.ToUInt32(type)) : "未声明",
            context is null ? null : Convert.ToUInt64(context),
            values.GetValueOrDefault("tokenizer.chat_template") as string ?? values.GetValueOrDefault("tokenizer.chat_template.default") as string ?? "");
    }
    private static string ReadString(BinaryReader r)
    {
        ulong length = r.ReadUInt64();
        if (length > int.MaxValue || length > (ulong)(r.BaseStream.Length - r.BaseStream.Position)) throw new InvalidDataException("GGUF 字符串长度无效。");
        return Encoding.UTF8.GetString(r.ReadBytes((int)length));
    }
    private static void Skip(BinaryReader r, ulong bytes)
    {
        if (bytes > (ulong)(r.BaseStream.Length - r.BaseStream.Position)) throw new EndOfStreamException("GGUF 元数据不完整。");
        r.BaseStream.Seek((long)bytes, SeekOrigin.Current);
    }
    private static object? ReadValue(BinaryReader r, uint type, bool keep)
    {
        if (type == 8)
        {
            if (keep) return ReadString(r);
            Skip(r, r.ReadUInt64()); return null;
        }
        if (type == 9)
        {
            uint element = r.ReadUInt32(); ulong count = r.ReadUInt64();
            int size = Size(element);
            if (size > 0) Skip(r, checked(count * (ulong)size));
            else for (ulong i = 0; i < count; i++) ReadValue(r, element, false);
            return null;
        }
        if (!keep) { int size = Size(type); if (size == 0) throw new InvalidDataException($"未知 GGUF 类型 {type}。"); Skip(r, (ulong)size); return null; }
        return type switch
        {
            0 => r.ReadByte(), 1 => r.ReadSByte(), 2 => r.ReadUInt16(), 3 => r.ReadInt16(),
            4 => r.ReadUInt32(), 5 => r.ReadInt32(), 6 => r.ReadSingle(), 7 => r.ReadBoolean(),
            10 => r.ReadUInt64(), 11 => r.ReadInt64(), 12 => r.ReadDouble(),
            _ => throw new InvalidDataException($"未知 GGUF 类型 {type}。")
        };
    }
    private static int Size(uint type) => type switch { 0 or 1 or 7 => 1, 2 or 3 => 2, 4 or 5 or 6 => 4, 10 or 11 or 12 => 8, _ => 0 };
    private static string Quant(uint type) => type switch
    {
        0 => "F32", 1 => "F16", 2 => "Q4_0", 3 => "Q4_1", 7 => "Q8_0", 8 => "Q5_0", 9 => "Q5_1",
        10 => "Q2_K", 11 => "Q3_K_S", 12 => "Q3_K_M", 13 => "Q3_K_L", 14 => "Q4_K_S", 15 => "Q4_K_M",
        16 => "Q5_K_S", 17 => "Q5_K_M", 18 => "Q6_K", 19 => "IQ2_XXS", 20 => "IQ2_XS", 21 => "Q2_K_S",
        22 => "IQ3_XS", 23 => "IQ3_XXS", 24 => "IQ1_S", 25 => "IQ4_NL", 26 => "IQ3_S", 27 => "IQ3_M",
        28 => "IQ2_S", 29 => "IQ2_M", 30 => "IQ4_XS", 31 => "IQ1_M", 32 => "BF16", 36 => "TQ1_0", 37 => "TQ2_0",
        38 => "MXFP4 MoE", 39 => "NVFP4", 40 => "Q1_0", 41 => "Q2_0", _ => $"类型 {type}"
    };
}
