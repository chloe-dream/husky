namespace Husky;

internal sealed class HuskyConfigException(string message, Exception? inner = null)
    : Exception(message, inner);
