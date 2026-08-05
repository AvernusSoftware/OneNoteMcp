using OneNoteMcp.Core.Configuration;

namespace OneNoteMcp.Tests;

internal static class PageXml
{
    public const string Ns = "http://schemas.microsoft.com/office/onenote/2013/onenote";

    public static readonly AgentOptions Agent = new() { DisplayName = "Test Agent", Initials = "TA" };

    public static string Page(string body, string title = "Test page", string? extraDefs = null) =>
        $$"""
         <?xml version="1.0"?>
         <one:Page xmlns:one="{{Ns}}" ID="{ID}{1}{B0}" name="{{title}}"
                   dateTime="2026-01-01T10:00:00.000Z" lastModifiedTime="2026-01-02T11:00:00.000Z">
           {{extraDefs}}
           <one:QuickStyleDef index="0" name="p" font="Calibri" fontSize="11.0"/>
           <one:QuickStyleDef index="1" name="h1" font="Calibri" fontSize="16.0"/>
           <one:QuickStyleDef index="2" name="h2" font="Calibri" fontSize="14.0"/>
           <one:QuickStyleDef index="3" name="h3" font="Calibri" fontSize="12.0"/>
           <one:QuickStyleDef index="4" name="code" font="Consolas" fontSize="10.0"/>
           <one:QuickStyleDef index="5" name="blockquote" font="Calibri" fontSize="11.0"/>
           <one:Title><one:OE><one:T><![CDATA[{{title}}]]></one:T></one:OE></one:Title>
           <one:Outline>
             <one:OEChildren>
               {{body}}
             </one:OEChildren>
           </one:Outline>
         </one:Page>
         """;

    public static string Oe(string cdata, string attributes = "") =>
        $"""<one:OE {attributes}><one:T><![CDATA[{cdata}]]></one:T></one:OE>""";

    public static string Bullet(string cdata, string? nested = null) =>
        $"""
         <one:OE>
           <one:List><one:Bullet bullet="2" fontSize="11.0"/></one:List>
           <one:T><![CDATA[{cdata}]]></one:T>
           {(nested is null ? string.Empty : $"<one:OEChildren>{nested}</one:OEChildren>")}
         </one:OE>
         """;

    public static string Number(string cdata) =>
        $"""
         <one:OE>
           <one:List><one:Number numberSequence="0" numberFormat="##."/></one:List>
           <one:T><![CDATA[{cdata}]]></one:T>
         </one:OE>
         """;

    public const string ToDoTagDef =
        """<one:TagDef index="0" type="3" symbol="3" name="To Do"/>""";

    public static string ToDo(string cdata, bool completed) =>
        $"""
         <one:OE>
           <one:Tag index="0" completed="{(completed ? "true" : "false")}"/>
           <one:T><![CDATA[{cdata}]]></one:T>
         </one:OE>
         """;
}
