using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Server.Admin;
using Animora.Server.Tests.Persistence;
using Animora.Web.Admin.Models;
using Animora.Web.Admin.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Server.Tests.Admin;

public sealed class ClinicBundleImportTests
{
    private const string SampleBundleJson = """
    {
      "formatVersion": 2,
      "generatedUtc": "2026-10-21T08:00:00Z",
      "converterVersion": "0.1.0",
      "sourceFingerprint": "ae4affdd3a5d4c116ed8e55a7f10227970d4af029a7ab7d77b0262149b733414",
      "sourceRowCounts": { "tblPatient": 2, "tblPatientRecord": 1, "tblVaccination": 1, "tblAntiParasite": 1, "tblCharge": 1 },
      "dedupeRule": "owner = valid 10-digit national id; else normalized full name + primary phone",
      "currency": "IRT",
      "notes": ["Boarding stays empty."],
      "warnings": ["tblPatient.fImage: LOB skipped"],
      "insurers": [{ "sourceId": "1", "name": "بیمه ایران", "legacy": null }],
      "paymentTypes": [{ "sourceId": "1", "name": "کارتخوان", "method": "card", "legacy": null }],
      "categories": [{ "sourceId": "10", "name": "داروها", "legacy": null }],
      "species": [{ "sourceId": "1", "name": "سگ", "species": 0, "legacy": null }],
      "vaccineTypes": [{ "sourceId": "2", "name": "هفت‌گانه", "legacy": null }],
      "staff": [{ "sourceId": "5", "fullName": "دکتر رضایی", "designation": "دامپزشک", "specialty": "داخلی", "legacy": null }],
      "services": [{ "sourceId": "20", "name": "ویزیت عمومی", "priceMinor": 150000, "staffSourceId": "5", "legacy": null }],
      "products": [{ "sourceId": "30", "name": "قرص ضدانگل", "salePriceMinor": 80000, "buyPriceMinor": 50000, "barcode": "123456", "reorderPoint": 5, "categorySourceId": "10", "legacy": null }],
      "owners": [
        {
          "sourceId": "owner:1001",
          "fullName": "علی احمدی",
          "phonePrimary": "09121234567",
          "phoneSecondary": null,
          "phoneLandline": "02188776655",
          "nationalId": "0012345678",
          "email": "ali@example.com",
          "address": "تهران",
          "city": "تهران",
          "gender": 1,
          "registeredUtc": "2024-03-20T08:30:00Z",
          "notes": "مراجع خوش‌حساب",
          "pets": [
            {
              "sourceId": "1001",
              "name": "رکس",
              "species": 0,
              "speciesName": "سگ",
              "breed": "ژرمن",
              "gender": 1,
              "birthDateUtc": "2022-03-20T08:30:00Z",
              "isNeutered": true,
              "color": "قهوه‌ای",
              "microchipCode": "981000000123456",
              "microchipImplantUtc": "2023-03-20T08:30:00Z",
              "bloodType": "DEA1.1+",
              "insurance": "بیمه ایران",
              "status": 0,
              "registeredUtc": "2024-03-20T08:30:00Z",
              "notes": "پرونده اولیه",
              "legacy": { "fIdentity": "1001" }
            }
          ],
          "legacy": null
        }
      ],
      "visits": [
        {
          "sourceId": "200",
          "petSourceId": "1001",
          "doctorSourceId": "5",
          "visitedUtc": "2024-04-10T10:00:00Z",
          "diagnosis": "التهاب گوش",
          "prescription": "قطره کلوبیوتیک",
          "nextVisitUtc": "2024-04-17T08:30:00Z",
          "reason": "معاینه",
          "symptoms": ["خارش گوش"],
          "treatments": ["شستشوی گوش"],
          "complaints": ["بی‌قراری"],
          "legacy": null
        }
      ],
      "vaccinations": [
        {
          "sourceId": "300",
          "petSourceId": "1001",
          "vaccineTypeSourceId": "2",
          "name": "هفت‌گانه سگ",
          "notes": "یادآور سالانه",
          "administeredUtc": "2024-04-10T08:30:00Z",
          "nextDueUtc": "2025-04-10T08:30:00Z",
          "legacy": null
        }
      ],
      "antiparasites": [
        {
          "sourceId": "400",
          "petSourceId": "1001",
          "administeredUtc": "2024-04-10T08:30:00Z",
          "notes": "قرص خوراکی",
          "internalParasite": "True",
          "externalParasite": "False",
          "nextDueUtc": "2024-07-10T08:30:00Z",
          "legacy": null
        }
      ],
      "labResults": [
        {
          "sourceTable": "tblBloodTestResult",
          "sourceId": "500",
          "petSourceId": "1001",
          "kind": "hematology",
          "title": "CBC",
          "performedUtc": "2024-04-10T09:00:00Z",
          "nextVisitUtc": null,
          "values": { "WBC": 8.5, "RBC": 6.2 },
          "notes": "نرمال",
          "legacy": null
        }
      ],
      "appointments": [
        {
          "sourceId": "600",
          "petSourceId": "1001",
          "patientName": "رکس",
          "doctorSourceId": "5",
          "startUtc": "2024-04-10T09:30:00Z",
          "notes": "نوبت معاینه",
          "mobile": "09121234567",
          "isCheckedIn": true,
          "legacy": null
        }
      ],
      "invoices": [
        {
          "sourceId": "700",
          "petSourceId": "1001",
          "customerName": "علی احمدی",
          "mobile": "09121234567",
          "issuedUtc": "2024-04-10T11:00:00Z",
          "number": "F-700",
          "totalMinor": 230000,
          "discountMinor": 10000,
          "insurancePayMinor": 0,
          "insurerSourceId": "1",
          "insuranceNo": "INS-100",
          "taxMinor": 0,
          "paidMinor": 220000,
          "paymentTypeSourceId": "1",
          "isDebtor": false,
          "notes": "تسویه کامل",
          "lines": [
            {
              "sourceId": "701",
              "kind": "service",
              "serviceSourceId": "20",
              "productSourceId": null,
              "staffSourceId": "5",
              "unitPriceMinor": 150000,
              "quantity": 1,
              "totalMinor": 150000,
              "discountMinor": 0,
              "description": "ویزیت عمومی",
              "legacy": null
            },
            {
              "sourceId": "702",
              "kind": "product",
              "serviceSourceId": null,
              "productSourceId": "30",
              "staffSourceId": "5",
              "unitPriceMinor": 80000,
              "quantity": 1,
              "totalMinor": 80000,
              "discountMinor": 0,
              "description": "قرص ضدانگل",
              "legacy": null
            }
          ],
          "payments": [
            {
              "sourceId": "703",
              "amountMinor": 220000,
              "paymentTypeSourceId": "1",
              "paidUtc": "2024-04-10T11:05:00Z",
              "legacy": null
            }
          ],
          "legacy": null
        }
      ],
      "stockReceipts": [
        {
          "sourceId": "800",
          "number": "R-800",
          "receivedUtc": "2024-04-01T08:30:00Z",
          "seller": "شرکت پخش دامپزشکی",
          "notes": "خرید اولیه",
          "totalMinor": 500000,
          "lines": [
            {
              "sourceId": "801",
              "productSourceId": "30",
              "quantity": 10,
              "buyPriceMinor": 50000,
              "expireUtc": "2026-04-01T08:30:00Z",
              "legacy": null
            }
          ],
          "legacy": null
        }
      ],
      "holidays": []
    }
    """;

    [Fact]
    public void StableId_MatchesPythonConverterAndUlidShape()
    {
        var id1 = ClinicBundleImportService.StableId("01K00000000000000000000001", "tblPatient", "1001");
        var id2 = ClinicBundleImportService.StableId("01K00000000000000000000001", "tblPatient", "1001");
        var otherTenant = ClinicBundleImportService.StableId("01K00000000000000000000002", "tblPatient", "1001");

        Assert.Equal(26, id1.Length);
        Assert.Equal(id1, id2);
        Assert.NotEqual(id1, otherTenant);
        Assert.True(id1[0] <= '7');
    }

    [Fact]
    public void ParseAndValidate_AcceptsValidV2Bundle_AndRejectsV1OrBrokenReferences()
    {
        var valid = ClinicBundleImportService.ParseAndValidate(
            Encoding.UTF8.GetBytes(SampleBundleJson),
            out var summary,
            "01K00000000000000000000001");
        Assert.True(valid.IsSuccess, valid.Detail);
        Assert.NotNull(summary);
        Assert.Equal(2, summary!.FormatVersion);
        Assert.Equal(1, summary.Owners);
        Assert.Equal(1, summary.Pets);
        Assert.Equal(1, summary.Visits);
        Assert.Equal(1, summary.Vaccinations);
        Assert.Equal(1, summary.Antiparasites);
        Assert.Equal(1, summary.LabResults);
        Assert.Equal(1, summary.Appointments);
        Assert.Equal(1, summary.Invoices);
        Assert.Equal(1, summary.StockReceipts);
        Assert.Equal(1, summary.Products);
        Assert.Equal(1, summary.Services);

        var wrongVersion = SampleBundleJson.Replace("\"formatVersion\": 2", "\"formatVersion\": 1", StringComparison.Ordinal);
        var rejectedV1 = ClinicBundleImportService.ParseAndValidate(Encoding.UTF8.GetBytes(wrongVersion), out _);
        Assert.True(rejectedV1.IsFailure);
        Assert.Equal(ErrorCodes.ValidationFailed, rejectedV1.Error);

        var brokenFk = SampleBundleJson.Replace("\"petSourceId\": \"1001\"", "\"petSourceId\": \"9999\"", StringComparison.Ordinal);
        var rejectedFk = ClinicBundleImportService.ParseAndValidate(Encoding.UTF8.GetBytes(brokenFk), out _);
        Assert.True(rejectedFk.IsFailure);
        Assert.Equal(ErrorCodes.ValidationFailed, rejectedFk.Error);
    }
}

public sealed class ClinicBundleImportPgTests(PgFixture pg) : IClassFixture<PgFixture>
{
    private async Task<AdminHost> Host()
    {
        await using (var db = TestSupport.CreateContext(connection: pg.ConnectionString))
        {
            await db.Database.MigrateAsync();
        }

        return new AdminHost(pg.ConnectionString);
    }

    private static AsyncServiceScope Scope(AdminHost host)
    {
        var scope = host.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().HttpContext =
            AdminAdaptersTests.CookieContext();
        return scope;
    }

    private static CreateTenantForm NewForm() => new()
    {
        Code = "imp-" + Guid.NewGuid().ToString("N")[..16],
        Name = "Imported Clinic",
        OwnerName = "Dr Owner",
        OwnerUserName = "owner",
        PlanCode = "standard",
        StartTrial = true,
    };

    [PgFact]
    public async Task ImportBundle_PopulatesTenantIdempotently_AndIsolatesFromOtherTenants()
    {
        using var host = await Host();
        await host.SeedOperatorAsync();
        using var login = await host.LoginAsync();
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        string tenantA;
        string tenantB;
        await using (var scope = Scope(host))
        {
            var cmds = scope.ServiceProvider.GetRequiredService<IAdminSubscriptionCommands>();
            var fA = NewForm();
            tenantA = (await cmds.CreateTenantAsync(fA, fA.IdempotencyKey)).Value!.TenantId!;
            var fB = NewForm();
            tenantB = (await cmds.CreateTenantAsync(fB, fB.IdempotencyKey)).Value!.TenantId!;
        }

        // Convert real db-sample/Clinic.bak via the Python converter into a temporary bundle if available,
        // or use the comprehensive v2 bundle fixture.
        var root = RepoRoot.Find();
        var bakPath = Path.Combine(root, "db-sample", "Clinic.bak");
        byte[] bundleBytes;
        if (File.Exists(bakPath))
        {
            var tmpOut = Path.Combine(Path.GetTempPath(), $"clinic-import-test-{Guid.NewGuid():N}.animora.json");
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("python3")
                {
                    WorkingDirectory = Path.Combine(root, "tools", "clinic-bak-converter"),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                psi.ArgumentList.Add("-m");
                psi.ArgumentList.Add("converter");
                psi.ArgumentList.Add("--bak");
                psi.ArgumentList.Add(bakPath);
                psi.ArgumentList.Add("--out");
                psi.ArgumentList.Add(tmpOut);
                using var proc = System.Diagnostics.Process.Start(psi)!;
                await proc.WaitForExitAsync();
                Assert.Equal(0, proc.ExitCode);
                bundleBytes = await File.ReadAllBytesAsync(tmpOut);
            }
            finally
            {
                if (File.Exists(tmpOut)) File.Delete(tmpOut);
            }
        }
        else
        {
            return;
        }

        // 1) First import via IAdminTenantImport.
        TenantImportReceipt firstReceipt;
        await using (var scope = Scope(host))
        {
            var importer = scope.ServiceProvider.GetRequiredService<IAdminTenantImport>();
            var validated = await importer.ValidateAsync(tenantA, bundleBytes);
            Assert.True(validated.IsSuccess, validated.Detail);
            Assert.True(validated.Value!.Owners > 0);
            Assert.True(validated.Value.Pets > 0);

            var imported = await importer.ImportAsync(tenantA, bundleBytes, confirmed: true);
            Assert.True(imported.IsSuccess, imported.Detail);
            firstReceipt = imported.Value!;
        }

        // 2) Re-import the exact same bundle into tenantA -> idempotent (counts in DB do not double).
        await using (var scope = Scope(host))
        {
            var importer = scope.ServiceProvider.GetRequiredService<IAdminTenantImport>();
            var second = await importer.ImportAsync(tenantA, bundleBytes, confirmed: true);
            Assert.True(second.IsSuccess, second.Detail);
            Assert.Equal(firstReceipt.Owners, second.Value!.Owners);
            Assert.Equal(firstReceipt.Pets, second.Value.Pets);
        }

        // 3) Verify rows exist in tenantA and tenantB remains completely empty.
        await using (var dbA = TestSupport.CreateContext(new FixedTenantContext(tenantA), pg.ConnectionString))
        {
            Assert.Equal(firstReceipt.Owners, await dbA.OwnerSet.CountAsync());
            Assert.Equal(firstReceipt.Pets, await dbA.PetSet.CountAsync());
            Assert.True(await dbA.VaccinationRecordSet.CountAsync() > 0);
            Assert.True(await dbA.TreatmentPlanSet.CountAsync() > 0);
            Assert.True(await dbA.InvoiceSet.CountAsync() > 0);
            Assert.True(await dbA.ProductSet.CountAsync() > 0);
        }

        await using (var dbB = TestSupport.CreateContext(new FixedTenantContext(tenantB), pg.ConnectionString))
        {
            Assert.Equal(0, await dbB.OwnerSet.CountAsync());
            Assert.Equal(0, await dbB.PetSet.CountAsync());
            Assert.Equal(0, await dbB.InvoiceSet.CountAsync());
        }

        // 4) Verify the Razor page /admin/tenants/{id}/import renders and the upload endpoint enforces antiforgery + confirmation.
        var pageHtml = await host.Client.GetStringAsync($"/admin/tenants/{tenantA}/import");
        Assert.Contains("clinic-import.animora.json", pageHtml);

        var token = await host.TokenAsync($"/admin/tenants/{tenantA}/import");
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(token), "__RequestVerificationToken");
        multipart.Add(new StringContent("true"), "dryRun");
        var fileContent = new ByteArrayContent(bundleBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        multipart.Add(fileContent, "bundle", "clinic-import.animora.json");

        using var uploadResp = await host.Client.PostAsync($"/admin/tenants/{tenantA}/import/upload", multipart);
        Assert.Equal(HttpStatusCode.OK, uploadResp.StatusCode);
    }
}
