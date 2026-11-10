using System.Buffers;
using System.Runtime.CompilerServices;

namespace GapSonar.Core.Shared.Infrastructure.Collections;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
/// <summary>Assembly-private owner of a rented reusable buffer.</summary>
internal struct PoolRental<T>(int minimumLength) : IDisposable
{
    private T[] _array = ArrayPool<T>.Shared.Rent(minimumLength);
    private int _length = minimumLength;
    private int _index = 0;

    public readonly Span<T> Span
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _array.AsSpan(0, _length);
    }

    public readonly Memory<T> Memory
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _array.AsMemory(0, _length);
    }

    public readonly ReadOnlyMemory<T> WrittenMemory => _array.AsMemory(0, _index);
    public readonly ReadOnlySpan<T> WrittenSpan => _array.AsSpan(0, _index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count)
    {
        _index += count;
        if (_index > _length) _length = _index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Memory<T> GetMemory(int sizeHint = 0)
    {
        CheckAndResize(sizeHint);
        return _array.AsMemory(_index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> GetSpan(int sizeHint = 0)
    {
        CheckAndResize(sizeHint);
        return _array.AsSpan(_index);
    }

    public void Write(ReadOnlySpan<T> value)
    {
        var span = GetSpan(value.Length);
        value.CopyTo(span);
        Advance(value.Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckAndResize(int sizeHint)
    {
        if (sizeHint == 0) sizeHint = 256;
        if (_index + sizeHint > _array.Length)
        {
            Resize(sizeHint);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Resize(int sizeHint)
    {
        int currentLength = _array.Length;
        int growBy = Math.Max(sizeHint, currentLength);
        int newSize = currentLength + growBy;

        var newArr = ArrayPool<T>.Shared.Rent(newSize);
        if (_index > 0)
        {
            Array.Copy(_array, 0, newArr, 0, _index);
        }

        ArrayPool<T>.Shared.Return(_array);
        _array = newArr;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (_array is not null)
        {
            ArrayPool<T>.Shared.Return(_array);
            _array = null!;
        }
    }
}
