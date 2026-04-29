namespace APGAnalyzer.Services.Edi;

/// <summary>
/// 835P parser — Professional Electronic Remittance Advice.
///
/// 835I and 835P share transaction set 835; the EDI structure is identical.
/// The business difference is what's *inside*: professional remittances
/// carry CPT/HCPCS codes (no UB-04 revenue codes) and modifier usage is
/// more common. We reuse the 835I parser internals and stamp claims with
/// FileType '835P'.
/// </summary>
public sealed class Edi835PParser
{
    public Parsed835IResult Parse(string text)
    {
        var inner = new Edi835IParser(text, fileType: "835P");
        return inner.Parse();
    }
}
