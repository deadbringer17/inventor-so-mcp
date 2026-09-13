#if INVENTOR2027
namespace Bimwright.Ipt.Shared.Plugin;

using System;
using System.Collections.Generic;
using Bimwright.Ipt.Shared.Infrastructure;
using Bimwright.Ipt.Shared.Handlers.SheetMetal;

/// <summary>
/// Sheet-metal registrar: rule/thickness, face, flange, cut, flat pattern and the read-only
/// sheet-metal probe. Inventor SO (2027) only, like the rest of the reviewed safe surface.
/// </summary>
public static partial class InventorCommandRegistry
{
    static partial void AddSheetMetal(Dictionary<string, IInventorCommand> d, Action<IInventorCommand> add)
    {
        add(new SetSheetMetalRuleHandler());
        add(new SheetMetalFaceHandler());
        add(new SheetMetalFlangeHandler());
        add(new SheetMetalCutHandler());
        add(new FlatPatternHandler());
        add(new SheetMetalInfoHandler());
        add(new SheetMetalHemHandler());
        add(new SheetMetalFoldHandler());
        add(new SheetMetalContourFlangeHandler());
        add(new SheetMetalCornerHandler(chamfer: false));
        add(new SheetMetalCornerHandler(chamfer: true));
        add(new SheetMetalUnfoldHandler(refold: false));
        add(new SheetMetalUnfoldHandler(refold: true));
        add(new SheetMetalPunchHandler());
        add(new SheetMetalRipHandler());
        add(new SheetMetalLoftedFlangeHandler());
    }
}
#endif
