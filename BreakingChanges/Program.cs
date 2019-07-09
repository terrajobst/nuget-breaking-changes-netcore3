using System.IO;
using System.Linq;

using Microsoft.Fx.ApiCataloging.ObjectModel;
using Microsoft.Fx.ApiCataloging.ObjectModel.Persistence;
using Microsoft.Fx.ApiCataloging.Sql;
using Microsoft.Fx.ApiUsages.Results;
using Microsoft.Fx.Csv;
using Microsoft.Fx.Progress;

namespace BreakingChanges
{
    class Program
    {
        static void Main(string[] args)
        {
            var usageData = @"\\fxcore\apps\NuGet\usage\NuGet.apiu";
            var platform1 = ".NET Core/2.2/Platform Extensions";
            var platform2 = ".NET Core/3.0";

            var usageResults = ConsoleRunner.Run(pm =>
            {
                pm.SetTask($"Loading NuGet usage data");
                pm.SetDetails(usageData);
                using (var fileStream = File.OpenRead(usageData))
                using (var progressStream = new ProgressStream(fileStream, pm))
                    return ApiUsageResults.Load(progressStream);
            });

            var apiCatalog = ConsoleRunner.Run(pm =>
            {
                pm.SetTask($"Loading API Catalog");
                var ds = SqlCatalogStores.ProductionDataSource;
                var db = SqlCatalogStores.ProductionDatabase;
                return ApiCatalogSqlServerFormat.Load(ds, db, pm);
            });

            var usageById = usageResults.Usages.ToDictionary(u => u.Id);
            var assemblyGroup1 = apiCatalog.AssemblyGroups.Single(ag => ag.AreaPath == platform1);
            var assemblyGroup2 = apiCatalog.AssemblyGroups.Single(ag => ag.AreaPath == platform2);

            var csvDocument = new CsvDocument("ID", "Namespace", "Type", "Member", "Package", "Path");
            using (var csvWriter = csvDocument.Append())
            {
                ConsoleRunner.Run(pm =>
                {
                    pm.SetTask($"Computing diff between {assemblyGroup1.Name} and {assemblyGroup2.Name}");
                    var apiCount = apiCatalog.GetAllApis().Count();
                    foreach (var api in apiCatalog.GetAllApis().WithProgress(apiCount, pm))
                    {
                        var isInAssemblyGroup1 = false;
                        var isInAssemblyGroup2 = false;

                        foreach (var ag in apiCatalog.GetContainedAssemblyGroups(api))
                        {
                            if (ag == assemblyGroup1)
                                isInAssemblyGroup1 = true;
                            else if (ag == assemblyGroup2)
                                isInAssemblyGroup2 = true;
                        }

                        var apiWasRemoved = isInAssemblyGroup1 && !isInAssemblyGroup2;
                        if (apiWasRemoved)
                        {
                            var apiId = api.DocId;
                            var apiNamespace = api.GetNamespaceName(apiCatalog);
                            var apiTypeName = api.GetTypeName(apiCatalog);
                            var apiMemberName = api.GetMemberName();

                            if (usageById.TryGetValue(api.DocId, out var usage))
                            {
                                var packages = usage.DataSeries.SelectMany(ds => ds)
                                                               .SelectMany(dp => usageResults.Assemblies[dp.AssemblyIndex].Applications)
                                                               .Distinct();

                                foreach (var package in packages)
                                {
                                    var packageName = package.Metadata["Name"];
                                    var packagePath = package.Metadata["Path"];

                                    csvWriter.Write(apiId);
                                    csvWriter.Write(apiNamespace);
                                    csvWriter.Write(apiTypeName);
                                    csvWriter.Write(apiMemberName);
                                    csvWriter.Write(packageName);
                                    csvWriter.Write(packagePath);
                                    csvWriter.WriteLine();
                                }
                            }
                            else
                            {
                                var packageName = "";
                                var packagePath = "";

                                csvWriter.Write(apiId);
                                csvWriter.Write(apiNamespace);
                                csvWriter.Write(apiTypeName);
                                csvWriter.Write(apiMemberName);
                                csvWriter.Write(packageName);
                                csvWriter.Write(packagePath);
                                csvWriter.WriteLine();
                            }
                        }
                    }
                });
            }

            csvDocument.ViewInExcel();
        }
    }
}
