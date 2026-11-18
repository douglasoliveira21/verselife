using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>
/// A small COLLADA model that uses most of what the importer promises (Z up and centimetres, a polylist, a material with a texture, a skeleton with skin weights
/// and an animation of one bone) is converted to a GLB and read back the way the world reads models.
/// </summary>
public partial class DaeImportSmoke : Node
{
    private const string OnePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private const string Dae = """
<?xml version="1.0" encoding="utf-8"?>
<COLLADA xmlns="http://www.collada.org/2005/11/COLLADASchema" version="1.4.1">
  <asset><unit meter="0.01" name="centimeter"/><up_axis>Z_UP</up_axis></asset>
  <library_images><image id="img"><init_from>textures/tex.png</init_from></image></library_images>
  <library_effects><effect id="fx"><profile_COMMON>
    <newparam sid="surf"><surface type="2D"><init_from>img</init_from></surface></newparam>
    <newparam sid="samp"><sampler2D><source>surf</source></sampler2D></newparam>
    <technique sid="common"><phong><diffuse><texture texture="samp" texcoord="UV"/></diffuse><shininess><float>20</float></shininess></phong></technique>
  </profile_COMMON></effect></library_effects>
  <library_materials><material id="mat" name="Paint"><instance_effect url="#fx"/></material></library_materials>
  <library_geometries><geometry id="geo"><mesh>
    <source id="pos"><float_array id="pos-a" count="12">0 0 0 100 0 0 100 100 0 0 100 0</float_array><technique_common><accessor source="#pos-a" count="4" stride="3"><param name="X" type="float"/><param name="Y" type="float"/><param name="Z" type="float"/></accessor></technique_common></source>
    <source id="uv"><float_array id="uv-a" count="8">0 0 1 0 1 1 0 1</float_array><technique_common><accessor source="#uv-a" count="4" stride="2"><param name="S" type="float"/><param name="T" type="float"/></accessor></technique_common></source>
    <vertices id="vert"><input semantic="POSITION" source="#pos"/></vertices>
    <polylist material="sym" count="1"><input semantic="VERTEX" source="#vert" offset="0"/><input semantic="TEXCOORD" source="#uv" offset="1" set="0"/><vcount>4</vcount><p>0 0 1 1 2 2 3 3</p></polylist>
  </mesh></geometry></library_geometries>
  <library_controllers><controller id="ctl"><skin source="#geo">
    <bind_shape_matrix>1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1</bind_shape_matrix>
    <source id="jn"><Name_array id="jn-a" count="2">Root Tip</Name_array><technique_common><accessor source="#jn-a" count="2" stride="1"><param name="JOINT" type="name"/></accessor></technique_common></source>
    <source id="ib"><float_array id="ib-a" count="32">1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1 1 0 0 0 0 1 0 -50 0 0 1 0 0 0 0 1</float_array><technique_common><accessor source="#ib-a" count="2" stride="16"><param name="TRANSFORM" type="float4x4"/></accessor></technique_common></source>
    <source id="wt"><float_array id="wt-a" count="1">1</float_array><technique_common><accessor source="#wt-a" count="1" stride="1"><param name="WEIGHT" type="float"/></accessor></technique_common></source>
    <joints><input semantic="JOINT" source="#jn"/><input semantic="INV_BIND_MATRIX" source="#ib"/></joints>
    <vertex_weights count="4"><input semantic="JOINT" source="#jn" offset="0"/><input semantic="WEIGHT" source="#wt" offset="1"/><vcount>1 1 1 1</vcount><v>0 0 0 0 1 0 1 0</v></vertex_weights>
  </skin></controller></library_controllers>
  <library_animations><animation id="a1" name="Wave">
    <source id="a1-t"><float_array id="a1-ta" count="2">0 1</float_array><technique_common><accessor source="#a1-ta" count="2" stride="1"><param name="TIME" type="float"/></accessor></technique_common></source>
    <source id="a1-v"><float_array id="a1-va" count="6">0 50 0 0 80 0</float_array><technique_common><accessor source="#a1-va" count="2" stride="3"><param name="X" type="float"/><param name="Y" type="float"/><param name="Z" type="float"/></accessor></technique_common></source>
    <sampler id="a1-s"><input semantic="INPUT" source="#a1-t"/><input semantic="OUTPUT" source="#a1-v"/></sampler>
    <channel source="#a1-s" target="Tip/location"/>
  </animation></library_animations>
  <library_visual_scenes><visual_scene id="sc">
    <node id="Root" sid="Root" type="JOINT"><translate sid="location">0 0 0</translate><node id="Tip" sid="Tip" type="JOINT"><translate sid="location">0 50 0</translate></node></node>
    <node id="Body" name="Body"><instance_controller url="#ctl"><skeleton>#Root</skeleton><bind_material><technique_common><instance_material symbol="sym" target="#mat"/></technique_common></bind_material></instance_controller></node>
  </visual_scene></library_visual_scenes>
  <scene><instance_visual_scene url="#sc"/></scene>
</COLLADA>
""";

    public override void _Ready()
    {
        try
        {
            var picture = Convert.FromBase64String(OnePixelPng);
            var asked = new List<string>();
            byte[]? Read(string reference) { asked.Add(reference); return reference.EndsWith("tex.png", StringComparison.Ordinal) ? picture : null; }

            Require(ColladaConverter.TryConvert(System.Text.Encoding.UTF8.GetBytes(Dae), Read, out var glb, out var error, out var warnings), "the COLLADA must convert: " + error);
            Require(asked.Count == 1 && warnings.Count == 0, $"the texture is asked for once and found: {string.Join(",", asked)} / {string.Join(" | ", warnings)}");

            var report = ModelInspector.Inspect(glb);
            Require(report.Valid, "the converted model passes the limits: " + report.Error);
            Require(report.Triangles == 2, $"the quad is two triangles: {report.Triangles}");
            Require(report.Materials == 1 && report.Textures == 1, $"one material with its texture: {report.Materials}/{report.Textures}");
            Require(report.Animations >= 1, $"the animation of the bone came along: {report.Animations}");
            // the file says Z up and centimetres: 100 cm along X is one metre, and the quad lies flat (its height is 0)
            Require(Mathf.Abs(report.Size.X - 1f) < .05f && report.Size.Y < .05f, $"Z up and centimetres are applied: {report.Size}");

            var skeletons = CountSkeletons(report.Scene);
            Require(skeletons == 1, $"the skeleton came along: {skeletons}");

            Require(!ColladaConverter.TryConvert("<COLLADA><library_geometries/></COLLADA>"u8.ToArray(), Read, out _, out var problem, out _) && problem is not null, "a COLLADA without meshes is refused with a reason");
            Require(!ColladaConverter.TryConvert("not xml at all"u8.ToArray(), Read, out _, out var notXml, out _) && notXml is not null, "a file that is not XML is refused with a reason");
            const string withDtd = "<?xml version=\"1.0\"?><!DOCTYPE COLLADA [<!ENTITY x \"y\">]><COLLADA/>";
            Require(!ColladaConverter.TryConvert(System.Text.Encoding.UTF8.GetBytes(withDtd), Read, out _, out var dtd, out _) && dtd is not null, "a DTD is refused");

            report.Scene?.Free();
            GD.Print("event=dae_import_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PrintErr("event=dae_import_smoke result=failed reason=" + exception.Message);
            GetTree().Quit(1);
        }
    }

    private static int CountSkeletons(Node? node) => node is null ? 0 : (node is Skeleton3D ? 1 : 0) + node.GetChildren().Sum(CountSkeletons);

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
