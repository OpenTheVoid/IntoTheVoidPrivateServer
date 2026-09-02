using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IntoTheVoidServer;

/// <summary>
/// YooAsset 资源清单 JSON 到二进制格式的转换器
/// 支持 FileVersion 2.0.0
/// </summary>
public static class YooAssetManifestConverter
{
    /// <summary>
    /// 文件标识签名 "YOO" (0x594F4F)
    /// </summary>
    private const uint FileSign = 0x594F4F;

    /// <summary>
    /// 将 JSON 格式的资源清单转换为二进制格式
    /// </summary>
    /// <param name="jsonContent">JSON 清单内容</param>
    /// <returns>二进制清单字节数组</returns>
    public static byte[] ConvertJsonToBinary(string jsonContent)
    {
        var manifest = JsonSerializer.Deserialize<YooAssetManifest>(jsonContent,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            }) ?? throw new InvalidOperationException("Failed to deserialize manifest JSON");

        var writer = new BufferWriter();

        // === 文件头部 ===
        writer.WriteUInt32(FileSign);                    // uint FileSign (0x594F4F)
        writer.WriteUTF8(manifest.FileVersion);           // string FileVersion
        writer.WriteBool(manifest.EnableAddressable);     // bool EnableAddressable
        writer.WriteBool(manifest.LocationToLower);       // bool LocationToLower
        writer.WriteBool(manifest.IncludeAssetGUID);      // bool IncludeAssetGUID
        writer.WriteInt32(manifest.OutputNameStyle);      // int OutputNameStyle
        writer.WriteUTF8(manifest.BuildPipeline);         // string BuildPipeline
        writer.WriteUTF8(manifest.PackageName);           // string PackageName
        writer.WriteUTF8(manifest.PackageVersion);        // string PackageVersion

        // === Asset 列表 ===
        var assetList = manifest.AssetList ?? [];
        writer.WriteInt32(assetList.Count);               // int AssetList.Count
        foreach (var asset in assetList)
        {
            writer.WriteUTF8(asset.Address);              // string Address
            writer.WriteUTF8(asset.AssetPath);            // string AssetPath
            writer.WriteUTF8(asset.AssetGUID);            // string AssetGUID
            writer.WriteUTF8Array(asset.AssetTags);       // string[] AssetTags
            writer.WriteInt32(asset.BundleID);            // int BundleID
        }

        // === Bundle 列表 ===
        var bundleList = manifest.BundleList ?? [];
        writer.WriteInt32(bundleList.Count);              // int BundleList.Count
        foreach (var bundle in bundleList)
        {
            writer.WriteUTF8(bundle.BundleName);          // string BundleName
            writer.WriteUInt32(bundle.UnityCRC);          // uint UnityCRC
            writer.WriteUTF8(bundle.FileHash);            // string FileHash
            writer.WriteUTF8(bundle.FileCRC);             // string FileCRC
            writer.WriteInt64(bundle.FileSize);           // long FileSize
            writer.WriteBool(bundle.Encrypted);           // bool Encrypted
            writer.WriteUTF8Array(bundle.Tags);           // string[] Tags
            writer.WriteInt32Array(bundle.DependIDs);     // int[] DependIDs
        }

        return writer.ToArray();
    }

    /// <summary>
    /// 计算二进制清单数据的 MD5 哈希值
    /// </summary>
    /// <param name="data">二进制数据</param>
    /// <returns>MD5 哈希字符串（32位小写十六进制）</returns>
    public static string ComputeMD5Hash(byte[] data)
    {
        var hashBytes = MD5.HashData(data);
        var sb = new StringBuilder(hashBytes.Length * 2);
        foreach (var b in hashBytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }

    /// <summary>
    /// 计算 JSON 清单转换为二进制后的 MD5 哈希值
    /// </summary>
    /// <param name="jsonContent">JSON 清单内容</param>
    /// <returns>MD5 哈希字符串（32位小写十六进制）</returns>
    public static string ComputeMD5HashFromJson(string jsonContent)
    {
        var binaryData = ConvertJsonToBinary(jsonContent);
        return ComputeMD5Hash(binaryData);
    }

    /// <summary>
    /// 从 rawfile 目录生成 Raw 包的二进制清单
    /// </summary>
    /// <param name="rawFilesDirectory">rawfile 文件所在目录</param>
    /// <param name="packageName">包名</param>
    /// <param name="packageVersion">包版本</param>
    /// <returns>二进制清单字节数组</returns>
    public static byte[] GenerateRawPackageManifest(string rawFilesDirectory, string packageName, string packageVersion)
    {
        var rawFiles = Directory.GetFiles(rawFilesDirectory, "*.rawfile");
        Array.Sort(rawFiles);

        var writer = new BufferWriter();

        // === 文件头部 ===
        writer.WriteUInt32(FileSign);
        writer.WriteUTF8("2.0.0");                    // FileVersion
        writer.WriteBool(false);                       // EnableAddressable
        writer.WriteBool(true);                        // LocationToLower
        writer.WriteBool(false);                       // IncludeAssetGUID
        writer.WriteInt32(0);                          // OutputNameStyle (HashName = 0? or BundleName?)
        writer.WriteUTF8("ScriptableBuildPipeline");   // BuildPipeline
        writer.WriteUTF8(packageName);                 // PackageName
        writer.WriteUTF8(packageVersion);              // PackageVersion

        // === Asset 列表 ===
        writer.WriteInt32(rawFiles.Length);            // AssetList.Count
        for (int i = 0; i < rawFiles.Length; i++)
        {
            var fileName = Path.GetFileName(rawFiles[i]);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            writer.WriteUTF8("");                      // Address
            writer.WriteUTF8(nameWithoutExt);          // AssetPath (use hash as path)
            writer.WriteUTF8("");                      // AssetGUID
            writer.WriteUTF8Array(null);               // AssetTags
            writer.WriteInt32(i);                      // BundleID
        }

        // === Bundle 列表 ===
        writer.WriteInt32(rawFiles.Length);            // BundleList.Count
        for (int i = 0; i < rawFiles.Length; i++)
        {
            var filePath = rawFiles[i];
            var fileName = Path.GetFileName(filePath);
            var fileInfo = new FileInfo(filePath);
            var hash = Path.GetFileNameWithoutExtension(fileName);

            writer.WriteUTF8(fileName);                // BundleName
            writer.WriteUInt32(0);                     // UnityCRC
            writer.WriteUTF8(hash);                    // FileHash
            writer.WriteUTF8("00000000");              // FileCRC
            writer.WriteInt64(fileInfo.Length);        // FileSize
            writer.WriteBool(false);                   // Encrypted
            writer.WriteUTF8Array(["raw"]);            // Tags
            writer.WriteInt32Array(null);              // DependIDs
        }

        return writer.ToArray();
    }

    /// <summary>
    /// 计算 Raw 包清单的 MD5 哈希
    /// </summary>
    public static string ComputeRawPackageManifestHash(string rawFilesDirectory, string packageName, string packageVersion)
    {
        var binaryData = GenerateRawPackageManifest(rawFilesDirectory, packageName, packageVersion);
        return ComputeMD5Hash(binaryData);
    }

    #region JSON 数据模型

    /// <summary>
    /// YooAsset 资源清单（JSON 模型）
    /// </summary>
    private class YooAssetManifest
    {
        public string FileVersion { get; set; } = string.Empty;
        public bool EnableAddressable { get; set; }
        public bool LocationToLower { get; set; }
        public bool IncludeAssetGUID { get; set; }
        public int OutputNameStyle { get; set; }
        public string BuildPipeline { get; set; } = string.Empty;
        public string PackageName { get; set; } = string.Empty;
        public string PackageVersion { get; set; } = string.Empty;
        public List<ManifestAsset>? AssetList { get; set; }
        public List<ManifestBundle>? BundleList { get; set; }
    }

    /// <summary>
    /// 清单中的 Asset 条目（JSON 模型）
    /// </summary>
    private class ManifestAsset
    {
        public string Address { get; set; } = string.Empty;
        public string AssetPath { get; set; } = string.Empty;
        public string AssetGUID { get; set; } = string.Empty;
        public string[]? AssetTags { get; set; }
        public int BundleID { get; set; }
    }

    /// <summary>
    /// 清单中的 Bundle 条目（JSON 模型）
    /// </summary>
    private class ManifestBundle
    {
        public string BundleName { get; set; } = string.Empty;
        public uint UnityCRC { get; set; }
        public string FileHash { get; set; } = string.Empty;
        public string FileCRC { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public bool Encrypted { get; set; }
        public string[]? Tags { get; set; }
        public int[]? DependIDs { get; set; }
    }

    #endregion
}

/// <summary>
/// 小端字节序的二进制缓冲区写入器
/// </summary>
public class BufferWriter
{
    private readonly MemoryStream _stream;
    private readonly BinaryWriter _writer;

    public BufferWriter()
    {
        _stream = new MemoryStream();
        _writer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: false);
    }

    /// <summary>
    /// 写入 4 字节无符号整数（小端）
    /// </summary>
    public void WriteUInt32(uint value)
    {
        _writer.Write(value);
    }

    /// <summary>
    /// 写入 4 字节有符号整数（小端）
    /// </summary>
    public void WriteInt32(int value)
    {
        _writer.Write(value);
    }

    /// <summary>
    /// 写入 8 字节有符号整数（小端）
    /// </summary>
    public void WriteInt64(long value)
    {
        _writer.Write(value);
    }

    /// <summary>
    /// 写入 1 字节布尔值
    /// </summary>
    public void WriteBool(bool value)
    {
        _writer.Write(value);
    }

    /// <summary>
    /// 写入 UTF8 字符串：先写 ushort (2字节) 长度，再写 UTF8 字节
    /// </summary>
    public void WriteUTF8(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            _writer.Write((ushort)0);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > ushort.MaxValue)
        {
            throw new ArgumentException($"String length exceeds ushort.MaxValue ({ushort.MaxValue} bytes)");
        }

        _writer.Write((ushort)bytes.Length);
        _writer.Write(bytes);
    }

    /// <summary>
    /// 写入 int 数组：先写 ushort (2字节) 长度，再写每个 int (4字节)
    /// </summary>
    public void WriteInt32Array(int[]? array)
    {
        if (array == null || array.Length == 0)
        {
            _writer.Write((ushort)0);
            return;
        }

        if (array.Length > ushort.MaxValue)
        {
            throw new ArgumentException($"Array length exceeds ushort.MaxValue ({ushort.MaxValue})");
        }

        _writer.Write((ushort)array.Length);
        foreach (var item in array)
        {
            _writer.Write(item);
        }
    }

    /// <summary>
    /// 写入 UTF8 字符串数组：先写 ushort (2字节) 长度，再写每个 UTF8 字符串
    /// </summary>
    public void WriteUTF8Array(string[]? array)
    {
        if (array == null || array.Length == 0)
        {
            _writer.Write((ushort)0);
            return;
        }

        if (array.Length > ushort.MaxValue)
        {
            throw new ArgumentException($"Array length exceeds ushort.MaxValue ({ushort.MaxValue})");
        }

        _writer.Write((ushort)array.Length);
        foreach (var item in array)
        {
            WriteUTF8(item);
        }
    }

    /// <summary>
    /// 获取写入的所有字节
    /// </summary>
    public byte[] ToArray()
    {
        _writer.Flush();
        return _stream.ToArray();
    }
}
