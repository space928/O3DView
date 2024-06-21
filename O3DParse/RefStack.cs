using System.Collections;
using System.Runtime.CompilerServices;

namespace O3DParse
{
    // Derived in part from:
    // https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/List.cs
    // MIT license
    internal class RefStack<T> : IEnumerable<T>, IEnumerable, IReadOnlyCollection<T>, ICollection
    {
        private T[] _array;
        private int _size;
        private int _version;

        private const int resizeThrehold = 8;

        public int Count => _size;

        public int Capacity => _array.Length;

        public bool IsSynchronized => false;

        public object SyncRoot => this;

        public RefStack()
        {
            _array = [];
        }

        public RefStack(int capacity)
        {
            _array = new T[capacity];
        }

        public void Push(T obj)
        {
            _version++;
            _size++;
            if (_size >= _array.Length)
                Resize();

            _array[_size - 1] = obj;
        }

        public T Pop()
        {
            _version++;
            var ret = _array[_size - 1];
            _size--;
            if (_size < _array.Length - resizeThrehold)
                Resize();
            return ret;
        }

        public T Peek(int depth = 0)
        {
            return _array[_size - depth - 1];
        }

        public ref T PeekRef()
        {
            return ref (new Span<T>(_array))[_size - 1];
        }

        public void Clear()
        {
            _size = 0;
            Resize();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Resize()
        {
            if (_size == 0)
            {
                _array = [];
                return;
            }

            var newArr = new T[_size + resizeThrehold];
            Array.Copy(_array, newArr, Math.Min(_array.Length, newArr.Length));
            //Buffer.BlockCopy(_array, 0, newArr, 0, Unsafe.SizeOf<T>() * _size);
            _array = newArr;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(Array array, int index)
        {
            Array.Copy(_array, 0, array, index, _size);
        }

        public IEnumerator<T> GetEnumerator() => new Enumerator(this);

        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

        public struct Enumerator : IEnumerator<T>, IEnumerator
        {
            //internal static IEnumerator<T>? s_emptyEnumerator;

            private readonly RefStack<T> _stack;
            private int _index;
            private readonly int _version;
            private T? _current;

            internal Enumerator(RefStack<T> stack)
            {
                _stack = stack;
                _index = 0;
                _version = stack._version;
                _current = default;
            }

            public readonly void Dispose() { }

            public bool MoveNext()
            {
                var local = _stack;

                if (_version == local._version && ((uint)_index < (uint)local._size))
                {
                    _current = local._array[_index];
                    _index++;
                    return true;
                }
                return MoveNextRare();
            }

            private bool MoveNextRare()
            {
                if (_version != _stack._version)
                    throw new InvalidOperationException("Stack has been modified since the enumerator was created!");

                _index = _stack._size + 1;
                _current = default;
                return false;
            }

            public readonly T Current => _current!;

            readonly object? IEnumerator.Current
            {
                get
                {
                    if (_index == 0 || _index == _stack._size + 1)
                        throw new InvalidOperationException("Index out of bound!");
                    return Current;
                }
            }

            void IEnumerator.Reset()
            {
                if (_version != _stack._version)
                    throw new InvalidOperationException("Stack has been modified since the enumerator was created!");

                _index = 0;
                _current = default;
            }
        }
    }
}
