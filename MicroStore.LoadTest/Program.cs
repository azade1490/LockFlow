using System.Text;
using NBomber.CSharp;

var client = new HttpClient();

var scenario = Scenario.Create("create_order", async context =>
{
    var response = await Step.Run("post_order", context, async () =>
    {
        var httpResponse = await client.PostAsync(
            "http://localhost:5001/PlaceOrder/PlaceOrder",
            new StringContent(
                """
                {
                    "ProductId": 1,
                    "Quantity": 1
                }
                """,
                Encoding.UTF8,
                "application/json"));

        return httpResponse.IsSuccessStatusCode
            ? Response.Ok()
            : Response.Fail();
    });

    return response;
})
.WithoutWarmUp()
//.WithLoadSimulations(
//    Simulation.InjectRate(
//        rate: 100,//این API تا چند درخواست در ثانیه را پاسخ می دهد؟
//        interval: TimeSpan.FromSeconds(1),
//        during: TimeSpan.FromMinutes(1))
//);
.WithLoadSimulations(
    Simulation.KeepConstant(
//هر کاربر به محض اتمام درخواست، درخواست بعدی را ارسال می کند.
//10 کاربر همزمان (Virtual User)
//اگر پاسخ API سریع باشد مثلاً 100 ms
//هر کاربر حدود 10 درخواست در ثانیه ارسال می کند و در مجموع نزدیک به
//10 × 10 = 100 Request / s
        copies: 10,
        during: TimeSpan.FromSeconds(10))
);

NBomberRunner
    .RegisterScenarios(scenario)
    .Run();


////روش ارسال 1000 درخواست همزمان با httpClient
//var client = new HttpClient
//{
//    BaseAddress = new Uri("http://localhost:5001")
//};

//const int requestCount = 1000;

//var start = new TaskCompletionSource();

//var tasks = Enumerable.Range(1, requestCount)
//    .Select(async i =>
//    {
//        await start.Task; // منتظر شروع همزمان

//        var response = await client.PostAsJsonAsync("/PlaceOrder/PlaceOrder", new
//        {
//            productId = 1,
//            quantity = 1
//        });

//        Console.WriteLine($"{i}: {response.StatusCode}");
//    });

//start.SetResult(); // همه تسک‌ها را همزمان آزاد کن

//await Task.WhenAll(tasks);

//Console.WriteLine("Finished.");

////روش ارسال 1000 درخواست همزمان با NBomber
