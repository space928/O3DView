namespace O3DParse;

[Serializable]
public class O3DException : Exception
{
    public O3DException() { }

    public O3DException(string? message) : base(message) { }

    public O3DException(string? message, Exception? innerException) : base(message, innerException) { }
}
