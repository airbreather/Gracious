namespace Gracious;

internal sealed class JankStringTemplateParameters
{
    private readonly string _sessionId;

    public JankStringTemplateParameters(uint sessionId)
    {
        _sessionId = $"{sessionId}";
    }

    public string Resolve(string template)
    {
        return template.Replace("{sessionId}", _sessionId);
    }
}
