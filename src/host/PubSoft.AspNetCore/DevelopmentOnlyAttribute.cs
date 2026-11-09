using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PubSoft.AspNetCore;

/// <summary>Marks a controller for development-only exposure (stripped by the convention outside Development).</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class DevelopmentOnlyAttribute : Attribute;
