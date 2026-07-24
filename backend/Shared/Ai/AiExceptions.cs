namespace Backend.Shared.Ai;

public class AiProviderConfigException(string message) : Exception(message);

public class AiProviderUnavailableException(string message) : Exception(message);

public class AiTextGenerationException(string message) : Exception(message);

public class AiImageGenerationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
