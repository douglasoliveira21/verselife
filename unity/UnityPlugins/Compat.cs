// APIs do .NET 5-8 que o netstandard2.1 (e portanto a Unity) não tem. Tudo internal: cada plugin carrega a sua cópia.
// build-plugins.ps1 troca as chamadas estáticas no código gerado (ver a tabela de substituições lá);
// os métodos de extensão e a PriorityQueue funcionam sem tocar nos fontes.
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace LifeVerse.Compat
{
    internal static class Compat
    {
        public static bool IsAsciiLetterOrDigit(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');

        public static double Log2(double x) => Math.Log(x, 2);

        public static bool IsDefined<T>(T value) where T : struct, Enum => Enum.IsDefined(typeof(T), value);

        public static string ToHexString(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public static byte[] Sha256(byte[] bytes) { using var sha = SHA256.Create(); return sha.ComputeHash(bytes); }

        public static byte[] Sha256(ReadOnlySpan<byte> bytes) => Sha256(bytes.ToArray());

        public static void WriteSingleLittleEndian(Span<byte> destination, float value) => BinaryPrimitives.WriteInt32LittleEndian(destination, BitConverter.SingleToInt32Bits(value));

        public static float ReadSingleLittleEndian(ReadOnlySpan<byte> source) => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(source));
    }
}

namespace System.IO
{
    internal static class StreamCompat
    {
        public static void ReadExactly(this Stream stream, byte[] buffer) => ReadExactly(stream, buffer, 0, buffer.Length);

        public static void ReadExactly(this Stream stream, byte[] buffer, int offset, int count)
        {
            var total = 0;
            while (total < count)
            {
                var read = stream.Read(buffer, offset + total, count - total);
                if (read <= 0) throw new EndOfStreamException();
                total += read;
            }
        }

        public static void ReadExactly(this Stream stream, Span<byte> buffer)
        {
            var total = 0;
            while (total < buffer.Length)
            {
                var read = stream.Read(buffer.Slice(total));
                if (read <= 0) throw new EndOfStreamException();
                total += read;
            }
        }
    }
}

namespace System.Linq
{
    internal static class LinqCompat
    {
        public static IOrderedEnumerable<T> Order<T>(this IEnumerable<T> source) => source.OrderBy(x => x);
        public static IOrderedEnumerable<T> OrderDescending<T>(this IEnumerable<T> source) => source.OrderByDescending(x => x);
    }
}

namespace System.Collections.Generic
{
    /// <summary>Mesma estrutura do PriorityQueue do .NET 6+ (heap de 4 filhos), para que empates saiam na mesma ordem.</summary>
    internal sealed class PriorityQueue<TElement, TPriority>
    {
        private (TElement Element, TPriority Priority)[] _nodes = new (TElement, TPriority)[4];
        private readonly IComparer<TPriority> _comparer;

        public PriorityQueue() : this(null) { }
        public PriorityQueue(IComparer<TPriority>? comparer) { _comparer = comparer ?? Comparer<TPriority>.Default; }

        public int Count { get; private set; }

        public void Enqueue(TElement element, TPriority priority)
        {
            if (Count == _nodes.Length) Array.Resize(ref _nodes, _nodes.Length * 2);
            MoveUp((element, priority), Count++);
        }

        public bool TryPeek(out TElement element, out TPriority priority)
        {
            if (Count == 0) { element = default!; priority = default!; return false; }
            (element, priority) = _nodes[0]; return true;
        }

        public TElement Peek() => Count == 0 ? throw new InvalidOperationException("The queue is empty.") : _nodes[0].Element;

        public bool TryDequeue(out TElement element, out TPriority priority)
        {
            if (Count == 0) { element = default!; priority = default!; return false; }
            (element, priority) = _nodes[0]; RemoveRoot(); return true;
        }

        public TElement Dequeue()
        {
            if (Count == 0) throw new InvalidOperationException("The queue is empty.");
            var element = _nodes[0].Element; RemoveRoot(); return element;
        }

        public void Clear() { Array.Clear(_nodes, 0, Count); Count = 0; }

        private void RemoveRoot()
        {
            var last = --Count;
            if (last > 0) MoveDown(_nodes[last], 0);
            _nodes[last] = default;
        }

        private void MoveUp((TElement Element, TPriority Priority) node, int index)
        {
            while (index > 0)
            {
                var parentIndex = (index - 1) >> 2; var parent = _nodes[parentIndex];
                if (_comparer.Compare(node.Priority, parent.Priority) < 0) { _nodes[index] = parent; index = parentIndex; } else break;
            }
            _nodes[index] = node;
        }

        private void MoveDown((TElement Element, TPriority Priority) node, int index)
        {
            int i;
            while ((i = (index << 2) + 1) < Count)
            {
                var min = _nodes[i]; var minIndex = i; var bound = Math.Min(i + 4, Count);
                while (++i < bound) { var next = _nodes[i]; if (_comparer.Compare(next.Priority, min.Priority) < 0) { min = next; minIndex = i; } }
                if (_comparer.Compare(node.Priority, min.Priority) <= 0) break;
                _nodes[index] = min; index = minIndex;
            }
            _nodes[index] = node;
        }
    }
}
