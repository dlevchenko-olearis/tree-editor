using TreeEditor.Api.Composition;

var builder = WebApplication.CreateBuilder(args);
builder.AddTreeEditor();

var app = builder.Build();
app.UseTreeEditor();
app.Run();
