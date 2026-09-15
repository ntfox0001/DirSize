using System.Collections.Concurrent;

namespace DirSize.Services;

/// <summary>
/// 目录大小的内存缓存（线程安全）。扫描完成一个目录就把其总大小写入，
/// 树/列表直接从缓存读取，避免重复计算。
/// </summary>
public sealed class SizeCache
{
    private readonly ConcurrentDictionary<string, long> _sizes = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string path, out long size) => _sizes.TryGetValue(path, out size);

    public long? Get(string path) => _sizes.TryGetValue(path, out var size) ? size : null;

    public void Store(string path, long size) => _sizes[path] = size;

    public void Clear() => _sizes.Clear();
}