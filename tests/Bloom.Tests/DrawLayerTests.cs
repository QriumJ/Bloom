using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using Bloom.Ocr;

namespace Bloom.Tests;

internal static class DrawLayerTests
{
    public static void PreferencesStayLocalAndUndoRedoKeepExportPixels()
    {
        var initial = new DrawPreferences("pen", 0, 1, 1, 1);
        var saved = new List<DrawPreferences>();
        var canvas = new Canvas();
        var layer = new DrawLayer(canvas, new Rect(12, 18, 64, 48), new Size(300, 200), _ => { }, initial, DrawTheme.Capture(), saved.Add);
        var ink = canvas.Children.OfType<InkCanvas>().Single();
        byte[] background = Enumerable.Repeat((byte)255, 128 * 96 * 4).ToArray();
        Check.True(ReferenceEquals(layer.Merge(background, 128, 96), background), "no ink returns original pixels");
        layer.AddForQa("pen", 2, new Point(8, 16), new Point(48, 16));
        var pen = layer.Merge(background, 128, 96);
        Check.True(!pen.SequenceEqual(background), "DIP ink is exported at the physical-pixel scale");
        Check.Eq(ink.DefaultDrawingAttributes.Width, 4.0, "pen keeps saved width");
        Check.True(ink.DefaultDrawingAttributes.FitToCurve, "existing pen smoothing stays enabled");
        Check.Eq(initial.PenColor, 0, "caller snapshot never changes");
        Check.Eq(saved.Last().PenColor, 2, "changed preference is a new immutable snapshot");
        Step(layer, true);
        Check.True(ReferenceEquals(layer.Merge(background, 128, 96), background), "undo restores plain capture");
        Step(layer, false);
        Check.True(layer.Merge(background, 128, 96).SequenceEqual(pen), "redo restores identical exported pixels");
        layer.AddForQa("mark", 1, new Point(8, 32), new Point(48, 32));
        Check.True(ink.DefaultDrawingAttributes.IsHighlighter, "highlighter behavior stays native");
        Check.Eq(ink.DefaultDrawingAttributes.StylusTip, StylusTip.Rectangle, "highlighter retains rectangular tip");
        Check.Eq(saved.Last().Tool, "mark", "tool preference remains independent of caller settings");
        var both = layer.Merge(background, 128, 96);
        ink.Strokes.Remove(ink.Strokes[0]); // same StrokesChanged contract as whole-stroke eraser
        Check.Eq(ink.Strokes.Count, 1, "whole-stroke erase removes one stroke");
        Step(layer, true);
        Check.True(layer.Merge(background, 128, 96).SequenceEqual(both), "undo erase restores identical export");
    }

    private static void Step(DrawLayer layer, bool back)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(DrawLayer);
        object undo = type.GetField("undo", flags)!.GetValue(layer)!;
        object redo = type.GetField("redo", flags)!.GetValue(layer)!;
        type.GetMethod("Step", flags)!.Invoke(layer, new[] { back ? undo : redo, back ? redo : undo, (object)back });
    }
}
