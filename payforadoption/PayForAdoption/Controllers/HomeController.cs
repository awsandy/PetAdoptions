﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

using Amazon.XRay.Recorder.Handlers.AwsSdk;
using Amazon.XRay.Recorder.Handlers.SqlServer;
using System.Data.SqlClient;
using System.Text.Json;
using Amazon.XRay.Recorder.Handlers.System.Net;
using Amazon.XRay.Recorder.Core;
using Microsoft.Extensions.Configuration;

namespace PayForAdoption.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HomeController : ControllerBase
    {
        private static SqlConnection _sqlConnection = new SqlConnection();
        private static HttpClient _httpClient = new HttpClient(new HttpClientXRayTracingHandler(new HttpClientHandler()));
        private static IConfiguration _configuration;
        public HomeController(IConfiguration configuration)
        {
            _configuration = configuration;
            AWSSDKHandler.RegisterXRayForAllServices();
        }

        [HttpPost("/api/home/CompleteAdoption")]
        public string CompleteAdoption([FromQuery] string petId, string pettype)
        {
            try
            {
                Console.WriteLine($"[{AWSXRayRecorder.Instance.GetEntity().TraceId}] - In CompleteAdoption Action method - PetId:{petId} - PetType:{pettype}");
                AWSXRayRecorder.Instance.AddAnnotation("PetId", petId);
                AWSXRayRecorder.Instance.AddAnnotation("PetType", pettype);
                
                _sqlConnection.ConnectionString = _configuration["rdsconnectionstring"];

                var sqlCommandText = $"INSERT INTO [dbo].[transactions] ([PetId], [Transaction_Id], [Adoption_Date]) VALUES ('{petId}', '{Guid.NewGuid().ToString()}', '{DateTime.Now.ToString()}')";

                AWSXRayRecorder.Instance.AddMetadata("Query",sqlCommandText);

                using (_sqlConnection)
                {
                    using var command = new TraceableSqlCommand(sqlCommandText, _sqlConnection);
                    command.Connection.Open();
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception e)
            {
                return e.Message;
            }

            AWSXRayRecorder.Instance.TraceMethod("UpdateAvailability", () => UpdateAvailability(petId,pettype));

            return "Success";
        }

        private static async Task<string> UpdateAvailability(string petId, string pettype)
        {
            AWSXRayRecorder.Instance.BeginSubsegment("Update Adoption Status");
            var updateResponse =  UpdateAdoptionStatus(petId, pettype).Result;
            AWSXRayRecorder.Instance.EndSubsegment();
            
            AWSXRayRecorder.Instance.BeginSubsegment("Invoking Availability API");
            var result = await _httpClient.GetStringAsync("https://amazon.com");
            AWSXRayRecorder.Instance.EndSubsegment();
            return "Complete";
        }
        
        private static async Task<object> UpdateAdoptionStatus(string petId, string petType)
        {
            var putParams = new PutParams() {petid = petId, pettype = petType};
            
            StringContent putData = new StringContent(JsonSerializer.Serialize(putParams));
            return await _httpClient.PutAsync(_configuration["updateadoptionstatusurl"], putData);
        }
    }
}