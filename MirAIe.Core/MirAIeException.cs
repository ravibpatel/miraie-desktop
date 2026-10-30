namespace MirAIe.Core;

public class MirAIeException(string message, Exception? innerException = null) : Exception(message, innerException);

public sealed class MirAIeAuthenticationException(string message) : MirAIeException(message);
