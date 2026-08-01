using System.Text.Json.Nodes;

using StreamFlow.App.Services.AI.Providers;

using Xunit;

namespace StreamFlow.Tests;

public class ComfyUiWorkflowTemplateTests
{
    private const string ValidWorkflow = """
    {
      "3": {
        "class_type": "KSampler",
        "inputs": { "seed": 1, "steps": 20, "positive": ["6", 0], "negative": ["7", 0] }
      },
      "5": {
        "class_type": "EmptyLatentImage",
        "inputs": { "width": 512, "height": 512 }
      },
      "6": { "class_type": "CLIPTextEncode", "inputs": { "text": "" } },
      "7": { "class_type": "CLIPTextEncode", "inputs": { "text": "" } },
      "4": { "class_type": "CheckpointLoaderSimple", "inputs": { "ckpt_name": "model.safetensors" } },
      "9": { "class_type": "SaveImage", "inputs": { "images": ["8", 0] } }
    }
    """;

    [Fact]
    public void Patch_SetsPromptOnThePositiveNode_NotTheNegativeOne()
    {
        var graph = ComfyUiWorkflowTemplate.Patch(ValidWorkflow, "a cat", "blurry", seed: null, steps: null, width: 512, height: 512, checkpointName: null);

        Assert.Equal("a cat", graph["6"]!["inputs"]!["text"]!.GetValue<string>());
        Assert.Equal("blurry", graph["7"]!["inputs"]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Patch_LeavesNegativeUnset_WhenNoNegativePromptGiven()
    {
        var graph = ComfyUiWorkflowTemplate.Patch(ValidWorkflow, "a cat", null, seed: null, steps: null, width: 512, height: 512, checkpointName: null);

        Assert.Equal("a cat", graph["6"]!["inputs"]!["text"]!.GetValue<string>());
        Assert.Equal("", graph["7"]!["inputs"]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Patch_SetsSeedAndStepsOnKSampler()
    {
        var graph = ComfyUiWorkflowTemplate.Patch(ValidWorkflow, "x", null, seed: 42, steps: 30, width: 512, height: 512, checkpointName: null);

        Assert.Equal(42, graph["3"]!["inputs"]!["seed"]!.GetValue<int>());
        Assert.Equal(30, graph["3"]!["inputs"]!["steps"]!.GetValue<int>());
    }

    [Fact]
    public void Patch_SetsWidthAndHeightOnEmptyLatentImage()
    {
        var graph = ComfyUiWorkflowTemplate.Patch(ValidWorkflow, "x", null, seed: null, steps: null, width: 768, height: 1024, checkpointName: null);

        Assert.Equal(768, graph["5"]!["inputs"]!["width"]!.GetValue<int>());
        Assert.Equal(1024, graph["5"]!["inputs"]!["height"]!.GetValue<int>());
    }

    [Fact]
    public void Patch_SetsCheckpointName_WhenProvided()
    {
        var graph = ComfyUiWorkflowTemplate.Patch(ValidWorkflow, "x", null, seed: null, steps: null, width: 512, height: 512, checkpointName: "myModel.safetensors");

        Assert.Equal("myModel.safetensors", graph["4"]!["inputs"]!["ckpt_name"]!.GetValue<string>());
    }

    [Fact]
    public void Patch_Throws_WhenKSamplerMissing()
    {
        const string noSampler = """{ "5": { "class_type": "EmptyLatentImage", "inputs": {} } }""";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ComfyUiWorkflowTemplate.Patch(noSampler, "x", null, null, null, 512, 512, null));
        Assert.Contains("KSampler", ex.Message);
    }

    [Fact]
    public void Patch_Throws_WhenPositiveLinkTargetIsMissingFromGraph()
    {
        const string danglingLink = """
        {
          "3": { "class_type": "KSampler", "inputs": { "positive": ["99", 0], "negative": ["7", 0] } },
          "5": { "class_type": "EmptyLatentImage", "inputs": {} },
          "7": { "class_type": "CLIPTextEncode", "inputs": { "text": "" } }
        }
        """;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ComfyUiWorkflowTemplate.Patch(danglingLink, "x", null, null, null, 512, 512, null));
        Assert.Contains("99", ex.Message);
    }

    [Fact]
    public void FindSaveImageNodeIds_ReturnsAllSaveImageNodes()
    {
        var graph = JsonNode.Parse(ValidWorkflow)!.AsObject();
        var ids = ComfyUiWorkflowTemplate.FindSaveImageNodeIds(graph);
        Assert.Equal(["9"], ids);
    }

    [Fact]
    public void LoadBundledDefault_ProducesAPatchableWorkflow()
    {
        // The actual shipped asset — verifies the bundled template itself satisfies every
        // structural requirement Patch enforces (KSampler + resolvable positive/negative +
        // EmptyLatentImage + a SaveImage node), not just the synthetic fixture above.
        var json = ComfyUiWorkflowTemplate.LoadBundledDefault();
        var graph = ComfyUiWorkflowTemplate.Patch(json, "a cat", "blurry", seed: 1, steps: 20, width: 512, height: 512, checkpointName: null);

        Assert.NotEmpty(ComfyUiWorkflowTemplate.FindSaveImageNodeIds(graph));
    }
}
