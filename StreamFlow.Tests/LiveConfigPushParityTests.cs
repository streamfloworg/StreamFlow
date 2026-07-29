using StreamFlow.App.Services.Core;
using StreamFlow.App.ViewModels.Pages;
using Xunit;

namespace StreamFlow.Tests;

/// <summary>Guards preview/stream WYSIWYG parity: <see cref="StreamSourceDef"/> is the only thing
/// the Rust compositor ever sees, but the WPF editor canvas positions its boxes straight from
/// <see cref="SourceSlot"/>/content properties — the composited preview and the live stream only
/// pick up an edit once <see cref="SceneEditorViewModel"/> pushes a fresh Config. That push is
/// gated by SceneEditorViewModel.GeometryPushTrigger{Slot,Content}Properties. If a wire field is
/// ever added to StreamSourceDef without a matching entry in those sets, the editor canvas and
/// the composited output silently diverge until an unrelated edit happens to trigger a push (see
/// ScheduleLiveConfigPush's doc comment). These tests fail the build the moment that happens,
/// instead of waiting for someone to notice a stale preview.</summary>
public class LiveConfigPushParityTests
{
    // Maps each slot-level StreamSourceDef wire field to the SourceSlot property that must be
    // registered in GeometryPushTriggerSlotProperties for edits to it to ever reach the
    // compositor. Add a field to StreamSourceDef? Add its mapping here.
    private static readonly Dictionary<string, string> SlotLevelWireFieldToTriggerProperty = new()
    {
        [nameof(StreamSourceDef.XPercent)] = nameof(SourceSlot.XPercent),
        [nameof(StreamSourceDef.YPercent)] = nameof(SourceSlot.YPercent),
        [nameof(StreamSourceDef.WPercent)] = nameof(SourceSlot.WPercent),
        [nameof(StreamSourceDef.HPercent)] = nameof(SourceSlot.HPercent),
        [nameof(StreamSourceDef.CornerRadiusPercent)] = nameof(SourceSlot.CornerRadiusPercent),
        [nameof(StreamSourceDef.Opacity)] = nameof(SourceSlot.OpacityPercent),
        [nameof(StreamSourceDef.RotationDegrees)] = nameof(SourceSlot.RotationDegrees),
    };

    // Wire fields deliberately outside the slot-level parity check, with why:
    //  - Identity fields: set once when a slot is created; edited by replacing the slot, not by
    //    mutating a property in place, so there's no "live edit" for a push trigger to catch.
    //  - Content-level fields: sourced from SourceSlot.Content, not the slot itself — checked
    //    against GeometryPushTriggerContentProperties in the test below instead.
    private static readonly HashSet<string> IdentityFields = [nameof(StreamSourceDef.SourceId), nameof(StreamSourceDef.IsPrimary)];
    private static readonly HashSet<string> ContentLevelFields = [nameof(StreamSourceDef.BlurRadius), nameof(StreamSourceDef.ChromaKey)];

    [Fact]
    public void StreamSourceDef_HasNoWireFieldUnaccountedForByThisTest()
    {
        var known = SlotLevelWireFieldToTriggerProperty.Keys.Concat(IdentityFields).Concat(ContentLevelFields).ToHashSet();
        var actual = typeof(StreamSourceDef).GetProperties().Select(p => p.Name).ToHashSet();
        var unaccounted = actual.Except(known).ToList();

        Assert.True(unaccounted.Count == 0,
            $"StreamSourceDef gained new field(s) this test doesn't know about: {string.Join(", ", unaccounted)}. " +
            "Add each to SlotLevelWireFieldToTriggerProperty (or IdentityFields/ContentLevelFields if it " +
            "genuinely needs no live-push trigger), then make sure SceneEditorViewModel's " +
            "GeometryPushTrigger*Properties sets are updated to match.");
    }

    [Fact]
    public void EverySlotLevelWireField_HasARegisteredPushTrigger()
    {
        foreach (var (wireField, triggerProperty) in SlotLevelWireFieldToTriggerProperty)
        {
            Assert.True(SceneEditorViewModel.GeometryPushTriggerSlotProperties.Contains(triggerProperty),
                $"StreamSourceDef.{wireField} maps to SourceSlot.{triggerProperty}, but that property isn't in " +
                "SceneEditorViewModel.GeometryPushTriggerSlotProperties — edits to it won't reach the compositor.");
        }
    }

    [Fact]
    public void EveryContentLevelWireField_HasARegisteredPushTrigger()
    {
        // BlurRadius and ChromaKey are each backed by multiple content-side properties.
        string[] expectedTriggers =
        [
            nameof(BlurOverlayContent.BlurRadius),
            nameof(IChromaKeyable.ChromaKeyEnabled),
            nameof(IChromaKeyable.ChromaKeyColor),
            nameof(IChromaKeyable.ChromaKeySimilarity),
        ];

        foreach (var triggerProperty in expectedTriggers)
        {
            Assert.True(SceneEditorViewModel.GeometryPushTriggerContentProperties.Contains(triggerProperty),
                $"Content property {triggerProperty} backs a StreamSourceDef field but isn't in " +
                "SceneEditorViewModel.GeometryPushTriggerContentProperties — edits to it won't reach the compositor.");
        }
    }
}
