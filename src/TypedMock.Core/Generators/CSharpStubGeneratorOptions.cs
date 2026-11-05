namespace TypedMock.Core.Generators;

public class CSharpStubGeneratorOptions
{
    public string Namespace { get; set; } = "Generated.Stubs";
    public string? ClassName { get; set; }
    public bool IsAbstract { get; set; } = false;
    public bool IncludeControllerAttributes { get; set; } = true;
    public bool IncludeExtensibilityHooks { get; set; } = true;
}
