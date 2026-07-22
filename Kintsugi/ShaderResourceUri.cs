namespace Kintsugi;

internal static class ShaderResourceUri
{
    public static Uri Get(string shaderName) => new($"pack://application:,,,/Kintsugi;component/Shaders/{shaderName}.cso", UriKind.Absolute);
}
