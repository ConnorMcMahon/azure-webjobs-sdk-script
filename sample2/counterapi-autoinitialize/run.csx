#r "Microsoft.WindowsAzure.Storage"
using System.Net;
using Microsoft.WindowsAzure.Storage.Table;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log, IQueryable<Counter> counterIn, ICollector<Counter> counterOut)
{
    HttpResponseMessage res = null;
    try
    {
        foreach(Counter counter in counterIn.Where(c=>c.RowKey == "Counter"))
        {
            counter.Value = 0;
            res = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Set all existing counters to 0")
            };
            return res;
        }

        if (res == null)
        {
            counterOut.Add(
                new Counter()
                {
                    RowKey = "Counter",
                    PartitionKey = "items",
                    Value = 0
                });
            res = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("The counter has been initialized")
            };
        }

    }
    catch (Exception ex)
    {
        res = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(ex.Message)
        };
    }


    return res;

}

public class Counter : TableEntity
{
    public int Value { get; set; }
}