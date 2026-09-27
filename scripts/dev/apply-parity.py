#!/usr/bin/env python3
"""One-time, assertion-checked integration edits for the feature branch. Removed after integration."""
from pathlib import Path

changes = {}
def replace(path, old, new):
    text = changes.get(path, Path(path).read_text())
    if new in text:
        return
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{path}: expected one integration anchor, found {count}: {old[:100]!r}')
    changes[path] = text.replace(old, new, 1)

replace('src/DrawingSpace.Visio/VisioReader.Styles.cs',
    'inherited = templateId is null ? root : root.DescendantsAndSelf().FirstOrDefault(e => e.Name.LocalName == "Shape" && VisioXml.Id(e) == templateId);',
    'var requestedTemplate = templateId;\n            inherited = requestedTemplate is null ? root : root.DescendantsAndSelf().FirstOrDefault(e => e.Name.LocalName == "Shape" && VisioXml.Id(e) == requestedTemplate);')
replace('src/DrawingSpace.Documents/DocumentJsonContext.cs',
    'WriteIndented = true, PropertyNamingPolicy', 'WriteIndented = true, IgnoreReadOnlyProperties = true, PropertyNamingPolicy')
replace('src/DrawingSpace.ShapeSheet/FormulaEngine.cs',
    'if (name == "VALUE") return n == 1 ? Evaluate(a.ToString(), resolve) : Arity();',
    'if (name == "VALUE")\n        {\n            if (n != 1) return Arity();\n            try { return Visit(FormulaParser.Parse(a.ToString()).Root, resolve, ref budget, depth + 1); }\n            catch (FormatException ex) { return FormulaValue.Error("#PARSE!", ex.Message); }\n        }')
replace('src/DrawingSpace.Editing/ShapeSheetScope.cs',
    'var length = new FormulaDimension(Length: 1);\n        return name.ToUpperInvariant() switch',
    'if (VisioCoordinateService.Builtin(page, shape, name) is { } imported) return imported;\n        var length = new FormulaDimension(Length: 1);\n        return name.ToUpperInvariant() switch')
replace('src/DrawingSpace.Editing/ShapeSheetScope.cs',
    'return page.Find(shape.ContainerId);', 'return page.Find(shape.FormulaParentId) ?? page.Find(shape.ContainerId);')
replace('src/DrawingSpace.Editing/ShapeSheetService.cs',
    'var pending = new List<Action>();', 'var coordinateChanges = VisioCoordinateService.Prepare(document, diagnostics);\n        var pending = new List<Action>();')
replace('src/DrawingSpace.Editing/ShapeSheetService.cs',
    'foreach (var change in pending) change();', 'foreach (var change in pending) change();\n        foreach (var change in coordinateChanges) change();')
replace('src/DrawingSpace.Editing/ShapeSheetService.cs',
    'shape.Width = Number("Width", shape.Width, 96, 1, 100000);', 'if (!shape.UsesVisioCoordinates)\n        {\n        shape.Width = Number("Width", shape.Width, 96, 1, 100000);')
replace('src/DrawingSpace.Editing/ShapeSheetService.cs',
    'if (values.TryGetValue("FlipY", out var flipY)) shape.FlipY = flipY.IsTrue;', 'if (values.TryGetValue("FlipY", out var flipY)) shape.FlipY = flipY.IsTrue;\n        }')
replace('src/DrawingSpace.Editing/ShapeSheetService.cs',
    'foreach (var (name, value) in changed)', 'VisioCoordinateService.ReplaceGeometryChanges(oldPage, old, page, shape, changed);\n                foreach (var (name, value) in changed)')
replace('src/DrawingSpace.Editing/EditorSession.cs',
    'MasterId = shape.MasterId, MasterShapeId = shape.MasterShapeId, ContainerId = shape.ContainerId,',
    'MasterId = shape.MasterId, MasterShapeId = shape.MasterShapeId, MasterInstanceId = shape.MasterInstanceId, ContainerId = shape.ContainerId,\n            UsesVisioCoordinates = shape.UsesVisioCoordinates, FormulaParentId = shape.FormulaParentId, CoordinateWidth = shape.CoordinateWidth, CoordinateHeight = shape.CoordinateHeight, IsGroupAnchor = shape.IsGroupAnchor,')
replace('src/DrawingSpace.Editing/MasterService.cs',
    'if (!instance.LocalOverrides.Contains(property.Name, StringComparer.OrdinalIgnoreCase)) property.Copy(instance, template);',
    'if (!(instance.UsesVisioCoordinates && property.Name is "Width" or "Height") && !instance.LocalOverrides.Contains(property.Name, StringComparer.OrdinalIgnoreCase)) property.Copy(instance, template);')
replace('src/DrawingSpace.Visio/VisioReader.Shapes.cs',
    'var localCells = VisioXml.ReadCells(local);',
    'var localCells = VisioXml.ReadCells(local);\n            foreach (var (cellName, cellValue) in shape.Cells)\n                if (!localCells.TryGetValue(cellName, out var localCell) || localCell.Inherited) cellValue.Inherited = true;')
replace('src/DrawingSpace.Visio/VisioReader.Shapes.cs',
    'Id = "group-" + nativeId, Name = shape.Name', 'Id = (string?)local.Attribute(VisioNamespaces.DrawingSpace + "GroupId") ?? "group-" + nativeId, Name = shape.Name')
replace('src/DrawingSpace.Editing/EditorSession.Commands.cs',
    'shape.LayerId = Page.Layers[0].Id; shape.Locked = false;',
    'shape.FormulaParentId = shape.FormulaParentId is { } formulaParent && map.TryGetValue(formulaParent, out var mappedParent) ? mappedParent : null;\n                shape.MasterInstanceId = shape.MasterInstanceId is { } instance && map.TryGetValue(instance, out var mappedInstance) ? mappedInstance : null;\n                shape.LayerId = Page.Layers[0].Id; shape.Locked = false;')
replace('src/DrawingSpace.Editing/EditorSession.Commands.cs',
    'var group = original.Clone(); group.Id = groups[original.Id]; group.VisioId = null;',
    'var group = original.Clone(); group.Id = groups[original.Id]; group.VisioId = null;\n                group.AnchorShapeId = group.AnchorShapeId is { } anchor && map.TryGetValue(anchor, out var mappedAnchor) ? mappedAnchor : null;')
replace('src/DrawingSpace.Documents/DocumentCodec.cs',
    'ValidateTree(document.Pages.ToDictionary(p => p.Id, p => p.BackgroundPageId), "background page");',
    'ValidateTree(document.Pages.ToDictionary(p => p.Id, p => p.BackgroundPageId), "background page");\n        AdvancedDocumentValidation.Validate(document);')

for path, content in changes.items():
    Path(path).write_text(content)
    print('Integrated', path)
print(f'{len(changes)} source files integrated')
