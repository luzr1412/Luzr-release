// Data-only access to the original Model Plot Painter geometry and renderer.
// Application startup, SAP adapters, Word and usage reporting are never invoked.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using Model_Plot_Painter;

public sealed class LuzrPlotData : ALProgram
{
    private readonly Dictionary<string, object> data;
    public readonly Dictionary<Plane, Dictionary<string, object>> Cuts = new Dictionary<Plane, Dictionary<string, object>>();
    public readonly Dictionary<Plane, double> ExactLocations = new Dictionary<Plane, double>();
    public readonly Dictionary<Plane, string> ManualKeys = new Dictionary<Plane, string>();
    public readonly Dictionary<Plane, string> CustomNames = new Dictionary<Plane, string>();
    public LuzrPlotData(Dictionary<string, object> value) { data = value; }
    public void Initialize() { Construct(); SetGrids(); }
    protected override string GetPath() { return Path.GetFullPath((string)data["model_path"]); }
    protected override LengthUnit GetLengthUnit() { return LengthUnit.mm; }
    protected override List<Node> BuildNodes()
    {
        var result = new List<Node>();
        int index = 0;
        foreach (Dictionary<string, object> n in (IEnumerable)data["joints"])
            result.Add(new Node((string)n["name"], ++index, Convert.ToDouble(n["x"]), Convert.ToDouble(n["y"]), Convert.ToDouble(n["z"])));
        return result;
    }
    protected override List<Member> BuildMembers()
    {
        var result = new List<Member>();
        var nodes = Nodes.ToDictionary(n => n.Label);
        int index = 0;
        foreach (Dictionary<string, object> f in (IEnumerable)data["frames"])
        {
            var member = new Member((string)f["name"], ++index, (string)f["section"], nodes[(string)f["joint_i"]], nodes[(string)f["joint_j"]], 0, null, null);
            result.Add(member);
        }
        return result;
    }
    protected override List<Area> BuildAreas()
    {
        var result = new List<Area>();
        var nodes = Nodes.ToDictionary(n => n.Label);
        int index = 0;
        foreach (Dictionary<string, object> a in (IEnumerable)data["shells"])
            result.Add(new Area((string)a["name"], ++index, ((IEnumerable)a["joints"]).Cast<string>().Select(name => nodes[name]).ToList()));
        return result;
    }
    public override void SelectMembers(IList<Member> members) { throw new InvalidOperationException("SAP selection is not supported by the data-only renderer."); }
    public void SetGrids()
    {
        GridLines = new List<GridLine>();
        foreach (Dictionary<string, object> g in (IEnumerable)data["grid_lines"])
            GridLines.Add(new GridLine((XYZ3D)Enum.Parse(typeof(XYZ3D), (string)g["axis"]), (string)g["id"], Convert.ToDouble(g["coordinate"])));
        // The original node-based plane builder uses null to denote no grid data.
        if (GridLines.Count == 0) GridLines = null;
        PMembers = new List<PMember>();
    }
    public void BuildDeclaredPlanes(IEnumerable cuts, Dictionary<string, object> basic)
    {
        Planes = new List<Plane>();
        var members = Members.ToDictionary(m => m.Label);
        var areas = Areas.ToDictionary(a => a.Label);
        var gridBuilder = typeof(ALProgram).GetMethod("BuildGridLineInPlaneView", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        foreach (Dictionary<string, object> cut in cuts)
        {
            string label = (string)cut["label"];
            var plane = new Plane((XYZ3D)Enum.Parse(typeof(XYZ3D), (string)cut["axis"]), Convert.ToDouble(cut["location"]), label.Length > 0, label);
            foreach (string name in (IEnumerable)cut["members"]) plane.AddMember(members[name]);
            foreach (string name in (IEnumerable)cut["areas"]) plane.AddArea(areas[name]);
            // AddMember/AddArea register their actual vertices with the original Plane.
            bool manual = cut.ContainsKey("manual_key");
            if (cut.ContainsKey("selected_key")) plane.Nodes.AddRange(((IEnumerable)cut["node_names"]).Cast<string>().Select(name => Nodes.Single(node => node.Label == name)).Where(node => !plane.Nodes.Contains(node)));
            if (plane.Nodes.Count == 0 && !manual) continue;
            plane.CalculatePlaneDim();
            if (GridLines != null) gridBuilder.Invoke(this, new object[] { plane });
            plane.CalculatePlaneDim();
            if (cut.ContainsKey("bounds"))
            {
                double[] bounds = ((IEnumerable)cut["bounds"]).Cast<object>().Select(Convert.ToDouble).ToArray();
                string[] properties = { "XMin", "XMax", "YMin", "YMax", "DX", "DY" };
                double[] values = { bounds[0], bounds[1], bounds[2], bounds[3], bounds[1]-bounds[0], bounds[3]-bounds[2] };
                for (int property = 0; property < properties.Length; property++) typeof(Plane).GetProperty(properties[property]).SetValue(plane, values[property], null);
                plane.SubPlaneIndex = Convert.ToInt32(cut["piece"]); plane.SubPlaneTotal = Convert.ToInt32(cut["piece_total"]);
                plane.Nodes.Clear(); plane.Nodes.AddRange(((IEnumerable)cut["node_names"]).Cast<string>().Select(name => Nodes.Single(node => node.Label == name)));
                if (plane.XGridLines != null) plane.XGridLines = plane.XGridLines.Where(g => g.XYZCoord >= plane.XMin && g.XYZCoord <= plane.XMax).ToList();
                if (plane.YGridLines != null) plane.YGridLines = plane.YGridLines.Where(g => g.XYZCoord >= plane.YMin && g.XYZCoord <= plane.YMax).ToList();
            }
            if (data.ContainsKey("hidden"))
            {
                var hidden = (Dictionary<string, object>)data["hidden"];
                var names = new HashSet<string>(((IEnumerable)hidden["joints"]).Cast<string>());
                plane.Nodes.RemoveAll(node => names.Contains(node.Label));
            }
            Cuts[plane] = cut;
            Planes.Add(plane);
            ExactLocations[plane] = Convert.ToDouble(cut["location"]);
            if (cut.ContainsKey("selected_key")) CustomNames[plane] = (string)cut["custom_name"];
            if (manual) { ManualKeys[plane] = (string)cut["manual_key"]; CustomNames[plane] = (string)cut["custom_name"]; }
        }
        Planes = Planes.OrderBy(p => p.XYZ == XYZ3D.Z ? 0 : p.XYZ == XYZ3D.X ? 1 : 2).ThenBy(p => ExactLocations[p]).ThenBy(p => p.SubPlaneIndex).ToList();
    }
}

public static class LuzrModelPlot
{
    private static void CustomTitle(string path, DrawingToolbox toolbox, string title)
    {
        using (var source = Image.FromFile(path))
        using (var bitmap = new Bitmap(source.Width, source.Height))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.DrawImageUnscaled(source, 0, 0);
            SizeF size = graphics.MeasureString(title, toolbox.TitleFont);
            graphics.DrawString(title, toolbox.TitleFont, Brushes.Black, (bitmap.Width - size.Width) / 2, bitmap.Height - toolbox.Margin / 2 - size.Height / 2);
            source.Dispose();
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
    private static string Number(double value) { return value.ToString("R", System.Globalization.CultureInfo.InvariantCulture); }
    private static bool AutoPlot(Plane plane)
    {
        if (plane.Areas.Count > 0) return true;
        double[] direction = null;
        foreach (var member in plane.Members)
        {
            // The plane's node/member API uses the original real endpoints.
            double dx = member.End.X - member.Start.X, dy = member.End.Y - member.Start.Y, dz = member.End.Z - member.Start.Z;
            double a = plane.XYZ == XYZ3D.X ? dy : dx, b = plane.XYZ == XYZ3D.Z ? dy : dz;
            double length = Math.Sqrt(a * a + b * b);
            if (length == 0) continue;
            a /= length; b /= length;
            if (direction != null && Math.Abs(a * direction[1] - b * direction[0]) > 1e-8) return true;
            if (direction == null) direction = new double[] { a, b };
        }
        return false;
    }
    private static object Call(object target, string name, params object[] arguments)
    {
        return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(target, arguments);
    }
    private static void Dimensions(DrawingToolbox toolbox, Plane plane, bool grid)
    {
        if (plane.Nodes.Count > 1)
        {
            object[] node = { plane.Nodes, null, null, null, null };
            Call(plane, "BuildNodeDimensionInfo", node);
            for (int axis = 0; axis < 2; axis++)
            {
                object lines = node[axis+1], positions = node[axis+3];
                if (grid)
                {
                    object[] merge = { (XY2D)axis, null, null, axis == 0 ? plane.XGridLines : plane.YGridLines, positions };
                    Call(toolbox, "MergeFootLineAndGridLine", merge); lines = merge[1]; positions = merge[2];
                }
                Call(toolbox, axis == 0 ? "DrawXDimLines" : "DrawYDimLines", lines, toolbox.NodeDimHeightRatio, positions);
            }
        }
        if (grid)
        {
            object[] lines = { null, null }; Call(plane, "BuildGridDimensionInfo", lines);
            Call(toolbox, "DrawXDimLines", lines[0], toolbox.GridDimHeightRatio, null);
            Call(toolbox, "DrawYDimLines", lines[1], toolbox.GridDimHeightRatio, null);
        }
    }
    private static string RenderViewport(DrawingToolbox toolbox, Plane plane, Dictionary<string, object> basic, double scale, bool custom, string directory)
    {
        typeof(DrawingToolbox).GetProperty("Plane").SetValue(toolbox, plane, null); toolbox.Scale = scale; Call(toolbox, "SetPlotDimension");
        try
        {
            if ((bool)basic["title"] && !custom) Call(toolbox, "PrintTitle");
            var upper = (MemberTagMode)Enum.Parse(typeof(MemberTagMode), (string)basic["upperTag"]);
            var lower = (MemberTagMode)Enum.Parse(typeof(MemberTagMode), (string)basic["lowerTag"]);
            if ((bool)basic["memberTagLegend"]) Call(toolbox, "PrintMemberTagLegend", upper, lower);
            Call(toolbox, "TranslateToOrigin");
            bool grids = (bool)basic["gridLines"] || (plane.XYZ != XYZ3D.Z && (bool)basic["elevationMarkers"]);
            if (grids) Call(toolbox, "DrawGridLine");
            var state = toolbox.Graphic.Save();
            float x0 = Convert.ToSingle(Call(toolbox, "TrnsX", plane.XMin, 0.0)), x1 = Convert.ToSingle(Call(toolbox, "TrnsX", plane.XMax, 0.0));
            float y0 = Convert.ToSingle(Call(toolbox, "TrnsY", plane.YMin, 0.0)), y1 = Convert.ToSingle(Call(toolbox, "TrnsY", plane.YMax, 0.0));
            float pad = Math.Max(toolbox.MemberLineWeight, toolbox.AreaLineWeight) / 2f;
            toolbox.Graphic.SetClip(new RectangleF(Math.Min(x0,x1)-pad, Math.Min(y0,y1)-pad, Math.Abs(x1-x0)+2*pad, Math.Abs(y1-y0)+2*pad));
            if ((bool)basic["fillArea"]) Call(toolbox, "DrawAreaFill", Convert.ToDouble(toolbox.AreaOpacity));
            var draw = typeof(DrawingToolbox).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single(method => method.Name == "DrawMembers").MakeGenericMethod(typeof(Member));
            draw.Invoke(toolbox, new object[] { upper, true, plane.Members });
            draw.Invoke(toolbox, new object[] { lower, false, plane.Members });
            Call(toolbox, "DrawAreaContour", (bool)basic["areaTag"]);
            toolbox.Graphic.Restore(state);
            // Real nodes on a shared border are retained in both viewports and drawn whole.
            Call(toolbox, "DrawNodes", (bool)basic["nodeTag"]);
            if ((bool)basic["dimensionLines"]) Dimensions(toolbox, plane, grids);
            if ((bool)basic["axes"]) Call(toolbox, "DrawGlobalAxes");
            return (string)Call(toolbox, "SaveImage", directory);
        }
        finally { toolbox.Graphic.Dispose(); toolbox.BitMap.Dispose(); }
    }
    public static void Run(string input, string output)
    {
        var json = new JavaScriptSerializer { MaxJsonLength = 100000000, RecursionLimit = 100 };
        var request = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(input));
        var settings = (Dictionary<string, object>)request["settings"];
        var basic = (Dictionary<string, object>)settings["basic"];
        var model = new LuzrPlotData((Dictionary<string, object>)request["model"]);
        model.Initialize();
        model.BuildDeclaredPlanes((IEnumerable)request["cuts"], basic);
        var toolbox = new DrawingToolbox(LengthUnit.mm, false);
        foreach (var item in (Dictionary<string, object>)settings["advanced"])
        {
            var property = typeof(DrawingToolbox).GetProperty(item.Key);
            if (property == null || !property.CanWrite || !property.GetSetMethod(true).IsPublic) throw new ArgumentException("Unknown drawing property: " + item.Key);
            if (item.Key.EndsWith("FontAsString"))
            {
                string familyName = ((string)item.Value).Split(',')[0].Trim();
                using (var installed = new System.Drawing.Text.InstalledFontCollection())
                    if (!installed.Families.Any(f => String.Equals(f.Name, familyName, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Font is not installed: " + familyName);
            }
            property.SetValue(toolbox, Convert.ChangeType(item.Value, property.PropertyType, System.Globalization.CultureInfo.InvariantCulture), null);
        }
        double dpiRatio = Convert.ToDouble(((Dictionary<string, object>)settings["output"])["dpi"]) / 96.0;
        toolbox.CvtRatio *= dpiRatio;
        foreach (var property in typeof(DrawingToolbox).GetProperties())
        {
            if (property.PropertyType == typeof(Font) && property.CanWrite && property.GetSetMethod(true).IsPublic)
            {
                var font = (Font)property.GetValue(toolbox, null);
                property.SetValue(toolbox, new Font(font.FontFamily, (float)(font.Size * dpiRatio), font.Style, font.Unit), null);
            }
            if (property.PropertyType == typeof(int) && property.CanWrite && property.GetSetMethod(true).IsPublic && !property.Name.EndsWith("AsArgb") && property.Name != "AreaOpacity")
                property.SetValue(toolbox, Convert.ToInt32(Convert.ToDouble(property.GetValue(toolbox, null)) * dpiRatio), null);
        }
        var planes = new List<object>();
        var selected = new HashSet<int>(((IEnumerable)request["selected"]).Cast<object>().Select(Convert.ToInt32));
        var operation = (string)request["operation"];
        for (int index = 0; index < model.Planes.Count; index++)
        {
            var plane = model.Planes[index];
            bool manual = model.ManualKeys.ContainsKey(plane);
            bool selectionView = model.Cuts[plane].ContainsKey("selected_key");
            if (plane.Members.Count == 0 && plane.Areas.Count == 0 && !manual && !selectionView && plane.Nodes.Count == 0) continue;
            double location = model.ExactLocations[plane];
            bool clipped = model.Cuts[plane].ContainsKey("bounds");
            string tile = clipped ? ":clip:" + Number(plane.XMin) + ":" + Number(plane.XMax) + ":" + Number(plane.YMin) + ":" + Number(plane.YMax) : plane.SubPlaneTotal > 1 ? ":tile:" + plane.SubPlaneIndex.ToString() + ":" + Number(plane.XMin) + ":" + Number(plane.XMax) + ":" + Number(plane.YMin) + ":" + Number(plane.YMax) : ":whole";
            string key = (string)basic["coordinateSystem"] + ":" + plane.XYZ.ToString() + ":" + Number(location) + tile;
            string baseKey = (string)basic["coordinateSystem"] + ":" + plane.XYZ.ToString() + ":" + Number(location) + ":whole";
            if (manual) { baseKey = "manual:" + model.ManualKeys[plane]; key = baseKey + (clipped ? tile : ""); }
            if (selectionView) key = baseKey = "selected:" + (string)model.Cuts[plane]["selected_key"];
            var scales = (Dictionary<string, object>)basic["viewScales"];
            double inheritedScale = baseKey != key && scales.ContainsKey(baseKey) ? Convert.ToDouble(scales[baseKey]) : Convert.ToDouble(basic["scale"]);
            double scale = scales.ContainsKey(key) ? Convert.ToDouble(scales[key]) : inheritedScale;
            int total = plane.SubPlaneTotal;
            plane.SubPlaneTotal = 1;
            string nativeName = plane.NameTitle(toolbox.TitlePrefixForPlanView, toolbox.TitlePrefixForElevView, toolbox.TitlePrefixForElevViewOnColLine);
            string name = manual || selectionView ? model.CustomNames[plane] : (string)model.Cuts[plane]["default_name"];
            plane.SubPlaneTotal = total;

            string defaultName = name;
            var viewNames = (Dictionary<string, object>)basic["viewNames"];
            if (!manual && viewNames.ContainsKey(baseKey)) name = ((string)viewNames[baseKey]).Trim();
            if (clipped) { name += " (" + plane.SubPlaneIndex.ToString() + ")"; defaultName += " (" + plane.SubPlaneIndex.ToString() + ")"; }
            if (viewNames.ContainsKey(key)) name = ((string)viewNames[key]).Trim();
            if (request.ContainsKey("display_names"))
            {
                var displayNames = (Dictionary<string, object>)request["display_names"];
                if (displayNames.ContainsKey(key)) name = (string)displayNames[key];
            }
            bool customTitle = manual || selectionView || clipped || name != nativeName;
            var item = new Dictionary<string, object> { { "id", index }, { "key", key }, { "name", name }, { "default_name", defaultName }, { "axis", plane.XYZ.ToString() }, { "location", location }, { "members", plane.Members.Count }, { "areas", plane.Areas.Count }, { "selected", selectionView || (!model.Cuts[plane].ContainsKey("discovered") && AutoPlot(plane)) }, { "discovered", model.Cuts[plane].ContainsKey("discovered") }, { "scale", scale }, { "default_scale", inheritedScale }, { "base_key", baseKey }, { "model_width", plane.DX }, { "model_height", plane.DY } };
            if (selectionView) item["selected_key"] = model.Cuts[plane]["selected_key"];
            if (manual) item["manual_key"] = model.ManualKeys[plane];
            if (clipped) { item["part"] = plane.SubPlaneIndex; item["parts"] = plane.SubPlaneTotal; }
            if (operation == "render" && selected.Contains(index))
            {
                if (plane.Members.Count == 0 && plane.Areas.Count == 0 && plane.Nodes.Count == 0) throw new ArgumentException("Selected view has no visible geometry: " + name);
                double effectiveScale = scale * DrawingToolbox.ExcelScalePersantage / 100.0;
                double width = plane.DX * toolbox.CvtRatio / effectiveScale + 2 * toolbox.Margin + toolbox.LeftsideSpace;
                double height = plane.DY * toolbox.CvtRatio / effectiveScale + 2 * toolbox.Margin + toolbox.UpsideSpace;
                if (width <= 0 || height <= 0 || width > 30000 || height > 30000 || width * height > 50000000)
                    {
                    double factor = Math.Min(30000.0 / Math.Max(width,height), Math.Sqrt(50000000.0/(width*height)));
                    int dpi = Convert.ToInt32(((Dictionary<string,object>)settings["output"])["dpi"]);
                    int supportedDpi = new int[] { 600, 300, 200, 150, 96 }.FirstOrDefault(value => value <= Math.Floor(dpi*factor));
                    string hint = supportedDpi > 0 ? "Split this view, increase its scale denominator, or choose DPI " + supportedDpi.ToString() + " or lower." : "No available DPI fits this view; add split positions or increase its scale denominator.";
                    throw new ArgumentException(String.Format(System.Globalization.CultureInfo.InvariantCulture, "View {0}: scale 1:{1}, DPI {2} requires {3:0} x {4:0} pixels ({5:0.0} MP). Limit: 50 MP / 30000 px per side. {6} Settings were not changed.", name, scale, dpi, width, height, width*height/1000000, hint));
                }
                string path;
                string viewDirectory = Path.Combine((string)request["directory"], "view-" + index.ToString());
                Directory.CreateDirectory(viewDirectory);
                var savedX = plane.XGridLines; var savedY = plane.YGridLines;
                if (!(bool)basic["gridLines"]) plane.XGridLines = new List<GridLine>();
                if (plane.XYZ == XYZ3D.Z ? !(bool)basic["gridLines"] : !(bool)basic["elevationMarkers"]) plane.YGridLines = new List<GridLine>();
                try
                {
                    if (clipped) path = RenderViewport(toolbox, plane, basic, scale, customTitle, viewDirectory);
                    else toolbox.CreatePlot(plane, (bool)basic["nodeTag"], (MemberTagMode)Enum.Parse(typeof(MemberTagMode), (string)basic["upperTag"]), (MemberTagMode)Enum.Parse(typeof(MemberTagMode), (string)basic["lowerTag"]), (bool)basic["memberTagLegend"], (bool)basic["areaTag"], scale, (bool)basic["fillArea"], (bool)basic["title"] && !customTitle, (bool)basic["axes"], (bool)basic["dimensionLines"], (bool)basic["gridLines"] || (plane.XYZ != XYZ3D.Z && (bool)basic["elevationMarkers"]), viewDirectory, out path);
                    if (customTitle && (bool)basic["title"]) CustomTitle(path, toolbox, name);
                }
                finally { plane.XGridLines = savedX; plane.YGridLines = savedY; }
                item["image"] = path;
                using (var image = Image.FromFile(path)) { item["width"] = image.Width; item["height"] = image.Height; }
            }
            planes.Add(item);
        }
        var plotted = new HashSet<string>(model.Planes.SelectMany(p => p.Members).Select(m => m.Label));
        var empty = ((IEnumerable)request["cuts"]).Cast<Dictionary<string, object>>().Where(c => !((IEnumerable)c["members"]).Cast<object>().Any() && !((IEnumerable)c["areas"]).Cast<object>().Any()).Select(c => (string)c["axis"] + "=" + Number(Convert.ToDouble(c["location"]))).ToArray();
        File.WriteAllText(output, json.Serialize(new { planes = planes, empty_planes = empty, unplotted = model.Members.Where(m => !plotted.Contains(m.Label)).Select(m => m.Label).ToArray() }), new System.Text.UTF8Encoding(false));
    }
}
