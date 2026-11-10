using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace ClientManagementSystem.Services;

public sealed record UserBioSlide(
    string Name,
    string Title,
    string Phone,
    string Bio,
    byte[]? Photo,
    string? PhotoContentType);

/// <summary>
/// One widescreen slide per user. Positions, labels, and Calibri sizes match the approved sample:
/// photo at the top left, name and phone to the right, biography below.
/// </summary>
public static class UserBioDeckBuilder
{
    private const string Font = "Calibri";
    private const string Navy = "0E2A47";
    private const string Body = "1C1C1C";
    private const string Muted = "5C6B7A";
    private const string Rule = "D5DEE7";
    private const string Placeholder = "F4F7FB";

    private const long SlideCx = 12192000;
    private const long SlideCy = 6858000;

    public static byte[] Build(IReadOnlyList<UserBioSlide> slides)
    {
        using var stream = new MemoryStream();
        using (var document = PresentationDocument.Create(stream, PresentationDocumentType.Presentation, true))
        {
            var presentationPart = document.AddPresentationPart();
            presentationPart.Presentation = new Presentation();

            var slideMasterPart = CreateSlideMaster(presentationPart);
            var slideLayoutPart = CreateSlideLayout(slideMasterPart);
            presentationPart.Presentation.SlideMasterIdList = new SlideMasterIdList(
                new SlideMasterId { Id = 2147483648U, RelationshipId = presentationPart.GetIdOfPart(slideMasterPart) });

            var slideIdList = new SlideIdList();
            presentationPart.Presentation.SlideIdList = slideIdList;
            presentationPart.Presentation.SlideSize = new SlideSize
            {
                Cx = (int)SlideCx,
                Cy = (int)SlideCy,
                Type = SlideSizeValues.Screen16x9
            };
            presentationPart.Presentation.NotesSize = new NotesSize { Cx = 6858000, Cy = 9144000 };

            uint slideId = 256;
            foreach (var slide in slides)
            {
                var slidePart = CreateSlide(presentationPart, slideLayoutPart, slide);
                slideIdList.Append(new SlideId
                {
                    Id = slideId++,
                    RelationshipId = presentationPart.GetIdOfPart(slidePart)
                });
            }

            presentationPart.Presentation.Save();
        }

        return stream.ToArray();
    }

    private static SlideMasterPart CreateSlideMaster(PresentationPart presentationPart)
    {
        var part = presentationPart.AddNewPart<SlideMasterPart>();
        part.SlideMaster = new SlideMaster(
            new CommonSlideData(EmptyTree()),
            new ColorMap
            {
                Background1 = A.ColorSchemeIndexValues.Light1,
                Text1 = A.ColorSchemeIndexValues.Dark1,
                Background2 = A.ColorSchemeIndexValues.Light2,
                Text2 = A.ColorSchemeIndexValues.Dark2,
                Accent1 = A.ColorSchemeIndexValues.Accent1,
                Accent2 = A.ColorSchemeIndexValues.Accent2,
                Accent3 = A.ColorSchemeIndexValues.Accent3,
                Accent4 = A.ColorSchemeIndexValues.Accent4,
                Accent5 = A.ColorSchemeIndexValues.Accent5,
                Accent6 = A.ColorSchemeIndexValues.Accent6,
                Hyperlink = A.ColorSchemeIndexValues.Hyperlink,
                FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink
            },
            new SlideLayoutIdList(),
            new TextStyles());
        return part;
    }

    private static SlideLayoutPart CreateSlideLayout(SlideMasterPart masterPart)
    {
        var part = masterPart.AddNewPart<SlideLayoutPart>();
        part.SlideLayout = new SlideLayout(new CommonSlideData(EmptyTree()));
        part.SlideLayout.Save();
        masterPart.SlideMaster!.SlideLayoutIdList!.Append(new SlideLayoutId
        {
            Id = 1U,
            RelationshipId = masterPart.GetIdOfPart(part)
        });
        masterPart.SlideMaster.Save();
        return part;
    }

    private static SlidePart CreateSlide(PresentationPart presentationPart, SlideLayoutPart layoutPart, UserBioSlide model)
    {
        var slidePart = presentationPart.AddNewPart<SlidePart>();
        slidePart.AddPart(layoutPart);

        var tree = EmptyTree();
        uint id = 2;
        tree.Append(SolidRect(id++, "Accent", 0, 0, SlideCx, Emu(0.12), Navy));

        if (TryImageContentType(model.Photo, model.PhotoContentType, out var imageContentType) && model.Photo is { Length: > 0 })
        {
            var imagePart = slidePart.AddImagePart(imageContentType);
            using var imageStream = new MemoryStream(model.Photo);
            imagePart.FeedData(imageStream);
            tree.Append(Picture(id++, slidePart.GetIdOfPart(imagePart), Emu(0.5), Emu(0.4), Emu(2.35), Emu(2.35)));
        }
        else
        {
            tree.Append(SolidRect(id++, "Photo", Emu(0.5), Emu(0.4), Emu(2.35), Emu(2.35), Placeholder));
        }

        tree.Append(TextBox(id++, "Name", Emu(3.15), Emu(0.42), Emu(9.6), Emu(0.62),
            [(model.Name, 32, true, Navy, 0)]));
        tree.Append(TextBox(id++, "Title", Emu(3.15), Emu(1.08), Emu(9.6), Emu(0.4),
            [(model.Title, 18, false, Body, 0)]));
        tree.Append(TextBox(id++, "Phone", Emu(3.15), Emu(1.55), Emu(9.6), Emu(0.85),
        [
            ("Phone", 14, true, Muted, 0),
            (model.Phone, 20, false, Body, 2)
        ]));
        tree.Append(SolidRect(id++, "Rule", Emu(0.5), Emu(2.95), Emu(12.33), Emu(0.015), Rule));
        tree.Append(TextBox(id++, "BioLabel", Emu(0.5), Emu(3.15), Emu(12.33), Emu(0.32),
            [("Bio", 14, true, Muted, 0)]));

        var paragraphs = string.IsNullOrWhiteSpace(model.Bio)
            ? Array.Empty<(string Text, int Size, bool Bold, string Color, int SpaceBefore)>()
            : model.Bio
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select((paragraph, index) => (paragraph.Replace("\n", " ", StringComparison.Ordinal), 16, false, Body, index == 0 ? 0 : 10))
                .ToArray();
        tree.Append(TextBox(id, "Bio", Emu(0.5), Emu(3.5), Emu(12.33), Emu(3.7), paragraphs));

        slidePart.Slide = new Slide(new CommonSlideData(tree));
        slidePart.Slide.Save();
        return slidePart;
    }

    private static ShapeTree EmptyTree()
    {
        return new ShapeTree(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                new P.NonVisualGroupShapeDrawingProperties(),
                new ApplicationNonVisualDrawingProperties()),
            new GroupShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = 0, Y = 0 },
                    new A.Extents { Cx = 0, Cy = 0 },
                    new A.ChildOffset { X = 0, Y = 0 },
                    new A.ChildExtents { Cx = 0, Cy = 0 })));
    }

    private static P.Shape SolidRect(uint id, string name, long x, long y, long cx, long cy, string rgb)
    {
        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new A.Transform2D(new A.Offset { X = x, Y = y }, new A.Extents { Cx = cx, Cy = cy }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                new A.SolidFill(new A.RgbColorModelHex { Val = rgb }),
                new A.Outline(new A.NoFill())),
            new P.TextBody(
                new A.BodyProperties(),
                new A.ListStyle(),
                new A.Paragraph(new A.EndParagraphRunProperties { Language = "en-US" })));
    }

    private static P.Shape TextBox(
        uint id,
        string name,
        long x,
        long y,
        long cx,
        long cy,
        IReadOnlyList<(string Text, int Size, bool Bold, string Color, int SpaceBefore)> lines)
    {
        var body = new P.TextBody(
            new A.BodyProperties
            {
                Wrap = A.TextWrappingValues.Square,
                Anchor = A.TextAnchoringTypeValues.Top,
                LeftInset = 0,
                RightInset = 0,
                TopInset = 0,
                BottomInset = 0
            },
            new A.ListStyle());

        if (lines.Count == 0)
        {
            body.Append(new A.Paragraph(new A.EndParagraphRunProperties { Language = "en-US", FontSize = 1600 }));
        }

        foreach (var line in lines)
        {
            var paragraph = new A.Paragraph();
            var properties = new A.ParagraphProperties { Alignment = A.TextAlignmentTypeValues.Left };
            properties.Append(new A.LineSpacing(new A.SpacingPercent { Val = 108000 }));
            if (line.SpaceBefore > 0)
            {
                properties.Append(new A.SpaceBefore(new A.SpacingPoints { Val = line.SpaceBefore * 100 }));
            }

            paragraph.Append(properties);
            paragraph.Append(new A.Run(
                RunProperties(line.Size, line.Bold, line.Color),
                new A.Text(line.Text)));
            body.Append(paragraph);
        }

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new A.Transform2D(new A.Offset { X = x, Y = y }, new A.Extents { Cx = cx, Cy = cy }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                new A.NoFill()),
            body);
    }

    private static A.RunProperties RunProperties(int sizePt, bool bold, string color)
    {
        var properties = new A.RunProperties
        {
            FontSize = sizePt * 100,
            Bold = bold,
            Language = "en-US",
            Dirty = false
        };
        properties.Append(new A.SolidFill(new A.RgbColorModelHex { Val = color }));
        properties.Append(new A.LatinFont { Typeface = Font });
        properties.Append(new A.EastAsianFont { Typeface = Font });
        properties.Append(new A.ComplexScriptFont { Typeface = Font });
        return properties;
    }

    private static P.Picture Picture(uint id, string relationshipId, long x, long y, long cx, long cy)
    {
        return new P.Picture(
            new P.NonVisualPictureProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = "Photo" },
                new P.NonVisualPictureDrawingProperties(new A.PictureLocks { NoChangeAspect = true }),
                new ApplicationNonVisualDrawingProperties()),
            new P.BlipFill(
                new A.Blip { Embed = relationshipId },
                new A.Stretch(new A.FillRectangle())),
            new P.ShapeProperties(
                new A.Transform2D(new A.Offset { X = x, Y = y }, new A.Extents { Cx = cx, Cy = cy }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }));
    }

    private static bool TryImageContentType(byte[]? photo, string? contentType, out string imageContentType)
    {
        imageContentType = "image/png";
        if (photo is not { Length: > 8 })
        {
            return false;
        }

        var type = (contentType ?? "").ToLowerInvariant();
        if (type.Contains("png", StringComparison.Ordinal) || (photo[0] == 0x89 && photo[1] == 0x50))
        {
            imageContentType = "image/png";
            return true;
        }

        if (type.Contains("jpeg", StringComparison.Ordinal) || type.Contains("jpg", StringComparison.Ordinal) || (photo[0] == 0xFF && photo[1] == 0xD8))
        {
            imageContentType = "image/jpeg";
            return true;
        }

        if (type.Contains("gif", StringComparison.Ordinal) || (photo[0] == 0x47 && photo[1] == 0x49))
        {
            imageContentType = "image/gif";
            return true;
        }

        return false;
    }

    private static long Emu(double inches) => (long)Math.Round(inches * 914400d);
}
