namespace CodeGame.Client;

public class CodeGameException : Exception
{
    public CodeGameException() : base() { }
    public CodeGameException(string? message) : base(message) { }
    public CodeGameException(string? message, Exception? innerException) : base(message, innerException) { }
}
