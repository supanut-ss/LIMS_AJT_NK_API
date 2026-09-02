using System.Text.Json;
using LIMS_AJT_NK_API.Services;

namespace LIMS_AJT_NK_API.Tests;

public class AjiApiRequestFactoryTests
{
    [Fact]
    public async Task CreateGetResultOcrRequest_MatchesV5Contract()
    {
        using var request = AjiApiRequestFactory.CreateGetResultOcrRequest(
            " http://dev-hippo.ztrus.net:6206/aji/get_result_ocr ",
            " token-1 ",
            " job-1_1 ");

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "http://dev-hippo.ztrus.net:6206/aji/get_result_ocr",
            request.RequestUri?.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("token-1", request.Headers.Authorization?.Parameter);
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        Assert.Equal("job-1_1", body.RootElement.GetProperty("job_task_id").GetString());
    }

    [Fact]
    public async Task CreateFeedbackRequest_MatchesV5ContractWithoutAuthorization()
    {
        using var ocrJson = JsonDocument.Parse("{\"product_name\":\"corrected\"}");
        using var request = AjiApiRequestFactory.CreateFeedbackRequest(
            "http://dev-hippo.ztrus.net:6206/aji/feedback",
            "job-1_1",
            ocrJson.RootElement.Clone());

        Assert.Null(request.Headers.Authorization);
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        Assert.Equal("job-1_1", body.RootElement.GetProperty("job_task_id").GetString());
        Assert.Equal(
            "corrected",
            body.RootElement.GetProperty("ocr_result").GetProperty("product_name").GetString());
    }

    [Theory]
    [InlineData("{\"status\":\"success\"}", true)]
    [InlineData("{\"status\":\"error\"}", false)]
    [InlineData("not-json", false)]
    [InlineData(null, false)]
    public void IsSuccessStatus_RequiresSuccessValue(string? responseBody, bool expected)
    {
        Assert.Equal(expected, AjiApiRequestFactory.IsSuccessStatus(responseBody));
    }
}
