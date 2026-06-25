using CreativeLongform.Application.Ollama;

namespace CreativeLongform.Application.Tests;

public sealed class OllamaBaseUrlHelperTests
{
    [Theory]
    [InlineData("http://192.168.1.50:11434", "http://192.168.1.50:11434/api")]
    [InlineData("http://192.168.1.50:11434/api", "http://192.168.1.50:11434/api")]
    [InlineData("http://192.168.1.50:11434/api/", "http://192.168.1.50:11434/api")]
    [InlineData("http://ai01.holovacs.local", "http://ai01.holovacs.local:11434/api")]
    [InlineData("http://ai01.holovacs.local/api", "http://ai01.holovacs.local:11434/api")]
    [InlineData("https://proxy.example.com/api", "https://proxy.example.com/api")]
    public void NormalizeApiRoot_appends_api_when_missing(string input, string expected)
    {
        Assert.Equal(expected, OllamaBaseUrlHelper.NormalizeApiRoot(input));
    }
}
