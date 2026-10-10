using Animora.Domain.Results;
namespace Animora.Web.Admin;

public static class AdminMessages
{
    private static readonly IReadOnlyDictionary<string,string> Messages=typeof(ErrorCodes).GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static)
        .Where(f=>f.FieldType==typeof(ErrorCode)).Select(f=>(ErrorCode)f.GetValue(null)!)
        .DistinctBy(e=>e.Code).ToDictionary(e=>e.Code,e=>e==ErrorCodes.ConcurrencyConflict?"Someone else changed this record. Reload before trying again.":
            e==ErrorCodes.OperatorCredentialsInvalid?"Unable to sign in. Check your credentials and try again.":
            e==ErrorCodes.InsightNotEnabled?"Insight is disabled until an explicit consent ADR is configured.":
            e==ErrorCodes.FileHashMismatch?"Only flagged mismatch files can be requeued or abandoned. Reload the list.":
            e==ErrorCodes.FileNotFound?"The file metadata could not be found.":
            e==ErrorCodes.ValidationFailed?"Check the form fields and try again.":"The operation could not be completed. Contact support with the error code.",StringComparer.Ordinal);
    public static bool HasMessage(string code)=>Messages.ContainsKey(code);
    public static string For(string code)=>Messages.GetValueOrDefault(code,"The operation could not be completed.");
}
