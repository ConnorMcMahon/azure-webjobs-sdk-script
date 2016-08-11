#r "Microsoft.WindowsAzure.Storage"
using System.Net;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Text;
using System.Collections;
using System.Net.Http;
using Microsoft.Azure.WebJobs.Script;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log, TableDictionary<string, TableDictionary<string, int>> counters)
{
    try
    {
        StringBuilder responseText = new StringBuilder();
        counters["mainpage"]["page1"] += 1;
        counters["faq"]["page2"] += 1;
        responseText.AppendLine("mainpage was visited " + (counters["mainpage"]["page1"] + counters["mainpage"]["page2"]) + " times");
        responseText.AppendLine("faq was visited " + (counters["faq"]["page1"] + counters["faq"]["page2"]) + " times");

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseText.ToString())
        };
    } catch (Exception e)
    {
        return new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(e.Message)
        };
    }
}