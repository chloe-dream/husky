namespace Husky;

internal sealed class UpdateException(string message, Exception? inner = null)
    : Exception(message, inner);
