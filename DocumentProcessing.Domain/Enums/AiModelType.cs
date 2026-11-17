namespace DocumentProcessing.Domain.Enums;

/// <summary>
/// Where the LLM that extracts invoice data runs. Stored as an integer: append new members, never reorder.
/// </summary>
public enum AiModelType
{
    /// <summary>A hosted API such as OpenAI, Azure OpenAI or Anthropic.</summary>
    Cloud = 0,

    /// <summary>A runtime on your own infrastructure, such as Ollama or a llama.cpp server.</summary>
    Local = 1
}
