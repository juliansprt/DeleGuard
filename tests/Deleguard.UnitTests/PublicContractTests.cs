using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;

namespace Deleguard.UnitTests;

public sealed class PublicContractTests
{
    [Fact]
    public void Published_contract_parses_and_all_schema_references_resolve()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "deleguard_dev_pack"))) root = root.Parent;
        Assert.NotNull(root);
        var spec = Path.Combine(root.FullName, "deleguard_dev_pack", "spec");
        var yaml = new YamlStream();
        using var reader = File.OpenText(Path.Combine(spec, "openapi.yaml"));
        yaml.Load(reader);
        var document = Assert.IsType<YamlMappingNode>(Assert.Single(yaml.Documents).RootNode);
        var schemas = (YamlMappingNode)((YamlMappingNode)document.Children["components"]).Children["schemas"];
        Visit(document);
        var request = JsonNode.Parse(File.ReadAllText(Path.Combine(spec, "authorization-request.schema.json")))!;
        Assert.Contains(request["required"]!.AsArray(), x => x!.ToString() == "operation");
        Assert.Equal("^[a-f0-9]{64}$", request["properties"]!["operation"]!["properties"]!["hash"]!["pattern"]!.ToString());

        void Visit(YamlNode node)
        {
            if (node is YamlMappingNode mapping)
            {
                foreach (var pair in mapping.Children)
                {
                    if (pair.Key.ToString() == "$ref")
                    {
                        var reference = pair.Value.ToString();
                        Assert.StartsWith("#/components/schemas/", reference);
                        Assert.True(schemas.Children.ContainsKey(reference["#/components/schemas/".Length..]), reference);
                    }
                    Visit(pair.Value);
                }
            }
            else if (node is YamlSequenceNode sequence) foreach (var child in sequence.Children) Visit(child);
        }
    }
}
