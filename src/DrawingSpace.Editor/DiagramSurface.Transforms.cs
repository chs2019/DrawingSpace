using DrawingSpace.Skia;
using SkiaSharp;

namespace DrawingSpace.Editor;

public sealed partial class DiagramSurface
{
    private SelectionTransformSnapshot? _selectionTransform;
    private Shape? _transformFrame;
    private Shape? _previewSelectionFrame;
    private bool _selectionTransformMoved;

    private static Shape CopyTransformFrame(Shape shape) => new()
    {
        X = shape.X, Y = shape.Y, Width = shape.Width, Height = shape.Height,
        Rotation = shape.Rotation, ShearX = shape.ShearX, FlipX = shape.FlipX, FlipY = shape.FlipY,
        Text = "", Name = "Selection"
    };

    private Shape? SelectionFrame()
    {
        if (_previewSelectionFrame is not null) return _previewSelectionFrame;
        if (Session is not { SelectedShapes.Count: > 1 } session) return null;
        var selected = session.SelectedShapes;
        // Imported groups have an explicit affine frame. Preserve that frame rather
        // than replacing a sheared or rotated group's handles with an axis-aligned box.
        if (selected[0].GroupId is { } id)
        {
            var root = session.Page.RootGroup(id);
            if (selected.All(s => s.GroupId is { } group && session.Page.RootGroup(group) == root))
            {
                var anchor = selected.FirstOrDefault(s => s.IsGroupAnchor && s.GroupId == root);
                if (anchor is not null) return CopyTransformFrame(anchor);
            }
        }
        var bounds = selected.Select(s => s.WorldBounds).Aggregate(RectD.Union);
        return new Shape { X = bounds.X, Y = bounds.Y, Width = Math.Max(1, bounds.Width), Height = Math.Max(1, bounds.Height), Text = "", Name = "Selection" };
    }

    private bool TryBeginSelectionTransform(PointD screen)
    {
        if (Session is not { SelectedShapes.Count: > 1 } session || SelectionFrame() is not { } frame) return false;
        var handles = ShapeTransforms.Handles(frame);
        var handle = Array.FindIndex(handles, p => session.Viewport.ToScreen(p).Distance(screen) <= 8);
        var rotate = handle < 0 && RotationHandle(frame).Distance(screen) <= 9;
        if (handle < 0 && !rotate) return false;
        // Capture rejects locked descendants before opening a transaction.
        var snapshot = session.CaptureSelectionTransform();
        session.Begin(rotate ? "Rotate selection" : "Resize selection");
        _selectionTransform = snapshot; _transformFrame = frame;
        _previewSelectionFrame = CopyTransformFrame(frame); _selectionTransformMoved = false;
        _handle = handle; _gesture = rotate ? Gesture.SelectionRotate : Gesture.SelectionResize;
        Invalidate(); return true;
    }

    private void MoveSelectionTransform(PointD world, VirtualKeyModifiers modifiers)
    {
        if (Session is not { } session || _selectionTransform is null || _transformFrame is null) return;
        if (!_selectionTransformMoved && (world - _startWorld).Length * session.Viewport.Zoom < 2) return;
        _selectionTransformMoved = true;
        var shift = IsShiftDown || modifiers.HasFlag(VirtualKeyModifiers.Shift);
        var frame = CopyTransformFrame(_transformFrame);
        MatrixD transform;
        if (_gesture == Gesture.SelectionResize)
        {
            // Corners keep the selection's aspect ratio; Shift also constrains side grips.
            ShapeTransforms.Resize(frame, _transformFrame, _handle, _startWorld, world, shift || _handle % 2 == 0);
            if (!_transformFrame.WorldMatrix.TryInvert(out var inverse)) throw new InvalidOperationException("Selection frame is singular.");
            transform = frame.WorldMatrix * inverse;
        }
        else
        {
            var center = _transformFrame.Bounds.Center;
            var start = Math.Atan2(_startWorld.Y - center.Y, _startWorld.X - center.X);
            var end = Math.Atan2(world.Y - center.Y, world.X - center.X);
            var angle = (end - start) * 180 / Math.PI;
            if (shift) angle = Math.Round((_transformFrame.Rotation + angle) / 15) * 15 - _transformFrame.Rotation;
            transform = MatrixD.Around(center, MatrixD.Rotation(angle));
            frame.ApplyWorldTransform(transform);
        }
        _selectionTransform.Apply(transform);
        _previewSelectionFrame = frame;
        session.Preview();
    }

    private void ResetAdvancedGestures()
    {
        _selectionTransform = null; _transformFrame = null; _previewSelectionFrame = null;
        _selectionTransformMoved = false; _segmentEditor = null; _connectorOriginal = null;
    }

    private void DrawSelectionAdorners(SKCanvas canvas)
    {
        if (Session is not { } session || SelectionFrame() is not { } frame) return;
        var corners = frame.WorldCorners.Select(session.Viewport.ToScreen).ToArray();
        using var path = SceneRenderer.Polyline(corners.Append(corners[0]).ToArray());
        using var outline = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#2B579A"), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
        using var fill = new SKPaint { IsAntialias = true, Color = SKColors.White };
        canvas.DrawPath(path, outline);
        if (session.SelectedShapes.Any(session.Page.IsLocked)) return;
        var handles = ShapeTransforms.Handles(frame).Select(session.Viewport.ToScreen).ToArray();
        foreach (var handle in handles)
        {
            var rect = new SKRect((float)handle.X - 4, (float)handle.Y - 4, (float)handle.X + 4, (float)handle.Y + 4);
            canvas.DrawRect(rect, fill); canvas.DrawRect(rect, outline);
        }
        var rotation = RotationHandle(frame); var top = handles[1];
        canvas.DrawLine((float)top.X, (float)top.Y, (float)rotation.X, (float)rotation.Y, outline);
        canvas.DrawCircle((float)rotation.X, (float)rotation.Y, 4.5f, fill);
        canvas.DrawCircle((float)rotation.X, (float)rotation.Y, 4.5f, outline);
    }
}
