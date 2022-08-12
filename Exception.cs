namespace CodeGame.Client;

/// <summary>
/// Thrown when CodeGame specific errors occur.
/// </summary>
public class CodeGameException : Exception
{
    /// <summary>
    /// Creates a new CodeGameException.
    /// </summary>
    public CodeGameException() : base() { }
    /// <summary>
    /// Creates a new CodeGameException with a message.
    /// </summary>
    public CodeGameException(string? message) : base(message) { }
    /// <summary>
    /// Creates a new CodeGameException with a message and an inner exception.
    /// </summary>
    public CodeGameException(string? message, Exception? innerException) : base(message, innerException) { }
}
