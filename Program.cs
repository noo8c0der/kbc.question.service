using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.Configure<DbConfig>(builder.Configuration);
builder.Services.AddSingleton<IDbClient, DbClient>();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// health check
builder.Services.AddHealthChecks()
    .AddCheck<DataBaseHealthCheck>("DbHealth");

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI();

app.UseHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            Status = report.Status.ToString(),
            Checks = report.Entries.Select(c => new
            {
                Component = c.Key,
                Status = c.Value.Status.ToString(),
                Description = c.Value.Description
            }),
            Duration = report.TotalDuration
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
});

app.UseHttpsRedirection();

app.MapGet("/question/get/{id}", async (string id, IDbClient dbClient) =>
    await dbClient.Database.GetCollection<Question>(nameof(Question))
        .Find(r => r.Id.Equals(id)).FirstOrDefaultAsync()
);

app.MapPost("/question/get", async ([FromBody] string[] ids, IDbClient dbClient) =>
{
    var filterDef = new FilterDefinitionBuilder<Question>();
    var filter = filterDef.In(r => r.Id, ids);
    return await dbClient.Database.GetCollection<Question>(nameof(Question)).Find(filter).ToListAsync();
});

app.MapPost("/question/add", async ([FromBody] List<Question> questions, IDbClient dbClient) =>
{
    foreach (var question in questions)
    {
        question.DateCreated = DateTime.Now;
        question.CreatedBy = ""; // TODO: set this value from token.
        question.DateLastModified = DateTime.Now;
        question.LastMantainedBy = ""; // TODO: set from token
        foreach (var ans in question.Answers)
        {
            ans.DateCreated = DateTime.Now;
            ans.CreatedBy = ""; // TODO: set this value from token.
            ans.DateLastModified = DateTime.Now;
            ans.LastMantainedBy = ""; // TODO: set from token
        }
    }

    await dbClient.Database.GetCollection<Question>(nameof(Question)).InsertManyAsync(questions);

    return Results.Created("", questions);
});

app.Run();

#region Health Checks
class DataBaseHealthCheck : IHealthCheck
{
    private readonly IDbClient _dbClient;
    public DataBaseHealthCheck(IDbClient dbClient) => _dbClient = dbClient ?? throw new ArgumentNullException(nameof(IDbClient));
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // just try to retrieve some document, doesn't matter it exists or not.
            // this ensures we are able to make connection with the database.
            _ = await _dbClient.Database.GetCollection<Question>(nameof(Question)).CountDocumentsAsync(d => d.Id == "82778f1ec9d144968ad39b53");
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            // some error occured while attemting to make a connection with database.
            // return Unhealthy response with error.
            return HealthCheckResult.Unhealthy(ex.Message);
        }
    }
}
#endregion

#region Models
public class BaseModel
{
    /// <summary>
    /// unique Id
    /// </summary>
    [BsonId]
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string Id { get; private set; }
    [JsonIgnore]
    [BsonElement("created_by")]
    public string CreatedBy { get; set; }
    [JsonIgnore]
    [BsonElement("date_created")]
    public DateTime DateCreated { get; set; }
    [JsonIgnore]
    [BsonElement("last_maintained_by")]
    public string LastMantainedBy { get; set; }
    [JsonIgnore]
    [BsonElement("date_last_modified")]
    public DateTime DateLastModified { get; set; }
}

public class Answer : BaseModel
{
    /// <summary>
    /// actual text
    /// </summary>
    public string Text { get; set; }
    /// <summary>
    /// answer is correct or not
    /// </summary>
    public bool IsCorrect { get; set; }
}

public class Question : BaseModel
{
    /// <summary>
    /// actual text
    /// </summary>
    public string QuestionText { get; set; }
    /// <summary>
    /// list of answer <see cref="Answer"/>
    /// </summary>
    public List<Answer> Answers { get; set; }
    /// <summary>
    /// Winning amount
    /// </summary>
    public decimal Amount { get; set; }
    /// <summary>
    /// ज्ञान
    /// </summary>
    public string Information { get; set; }
}

public class DbConfig
{
    public string ConnectionString { get; set; }
    public string DbName { get; set; }
}
#endregion

#region DbContext
public interface IDbClient
{
    public IMongoDatabase Database { get; init; }
}

public class DbClient : IDbClient
{
    public IMongoDatabase Database { get; init; }
    public DbClient(IOptions<DbConfig> dbConfig)
    {
        var client = new MongoClient(dbConfig.Value.ConnectionString ?? "mongo://localhost:27017");
        Database = client.GetDatabase(dbConfig.Value.DbName);
    }
}
#endregion