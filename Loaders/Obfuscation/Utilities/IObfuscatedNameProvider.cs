namespace Loaders.Obfuscation.Utilities
{
    public interface IObfuscatedNameProvider
    {
        string Generate(string originalName);
    }
}
