using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class StateUtility
    {
        private static string _scalarCode = @"private class ScalarEntity<T> : TableEntity
{
    //needs to be a generic Type, as if Value is of type object it will be loaded into Table storage as NULL
    public T Value { get; set; }

    public ScalarEntity()
    {
    }
}";
        [CLSCompliant(false)]
        public class ScalarEntity<T> : TableEntity
        {
            //needs to be a generic Type, as if Value is of type object it will be loaded into Table storage as NULL
            public T Value { get; set; }

            public ScalarEntity()
            {
            }
        }

        private static Tuple<int,int> FindFunctionBody(string functionCode)
        {
            int functionStartIndex = functionCode.IndexOf("public static async Task<HttpResponseMessage> Run(", StringComparison.CurrentCulture);
            string postHeaderCode = functionCode.Substring(functionStartIndex);
            int bodyStartIndex = postHeaderCode.IndexOf("{", StringComparison.CurrentCulture) + functionStartIndex+1;
            //this is flawed if they have state changing code in the return statement
            int bodyEndIndex = postHeaderCode.IndexOf("return ", StringComparison.CurrentCulture)+functionStartIndex;
            return new Tuple<int, int>(bodyStartIndex, bodyEndIndex);
        }

        private static string ReadGlobalVariablesCode(CloudTable table, string partitionKey, Dictionary<string, Type> variables)
        {
            TableQuery<ScalarEntity<object>> retrieveQuery = new TableQuery<ScalarEntity<object>>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, partitionKey));
            EntityResolver<ScalarEntity<object>> scalarResolver = (pk, rk, ts, props, etag) =>
            {
                ScalarEntity<object> resolvedEntity = new ScalarEntity<object>();

                resolvedEntity.PartitionKey = pk;
                resolvedEntity.RowKey = rk;
                resolvedEntity.Timestamp = ts;
                resolvedEntity.ETag = etag;
                resolvedEntity.Value = Convert.ChangeType(props["Value"].PropertyAsObject, variables[rk]);
                return resolvedEntity;
            };

            var globals = table.ExecuteQuery(retrieveQuery,scalarResolver);
            StringBuilder codeBuilder = new StringBuilder();
            string scalarTemplate = "\t{0} {1} = {2};";
            foreach (var globalVariable in globals)
            {
                string varName = globalVariable.RowKey;
                codeBuilder.AppendLine(string.Format(scalarTemplate, variables[varName], varName, globalVariable.Value));
            }
            return codeBuilder.ToString();
        }

        private static string WriteGlobalVariablesCode(string connectionString, string tableName, string partitionKey, Dictionary<string, Type> variables)
        {
            StringBuilder codeBuilder = new StringBuilder();
            //code to generate cloud table object
            codeBuilder.AppendLine(
                string.Format(@"CloudStorageAccount storageAccount = CloudStorageAccount.Parse(""{0}"");", connectionString));
            codeBuilder.AppendLine("CloudTableClient client = storageAccount.CreateCloudTableClient();");
            codeBuilder.AppendLine(string.Format(@"CloudTable table = client.GetTableReference(""{0}"");", tableName));
            //code to generate operations
            codeBuilder.AppendLine("TableBatchOperation batchOperation = new TableBatchOperation();");
            foreach (var variable in variables)
            {
                //checks if the variable is a "scalar" type, and if so, creates a ScalarEntity<T> and inserts it into Azure Storage
                Type type = variable.Value;
                if (type.IsPrimitive || type == typeof(Decimal) || type == typeof(string))
                {
                    codeBuilder.AppendLine(string.Format("ScalarEntity<{0}> scalar_{1} = new ScalarEntity<{0}>();",
                        variable.Value, variable.Key));
                    codeBuilder.AppendLine(string.Format(@"scalar_{0}.PartitionKey = ""{1}"";", variable.Key, partitionKey));
                    codeBuilder.AppendLine(string.Format(@"scalar_{0}.RowKey = ""{0}"";", variable.Key));
                    codeBuilder.AppendLine(string.Format("scalar_{0}.Value = {0};", variable.Key));
                    codeBuilder.AppendLine(string.Format("batchOperation.InsertOrReplace(scalar_{0});", variable.Key));
                }
            }
            codeBuilder.AppendLine("table.ExecuteBatch(batchOperation);");


            return codeBuilder.ToString();
        }

        public static string TranslateCodeForState(string functionCode, Dictionary<string, Type> variables, TableDetails details)
        {
            if (variables == null || variables.Count == 0)
            {
                return functionCode;
            }
            //Create a table object given the table storage information.
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
            CloudTableClient client = storageAccount.CreateCloudTableClient();
            CloudTable table = client.GetTableReference(details.Table);

            ReadGlobalVariablesCode(table, details.PartitionKey, variables);
            var bodyIndices = FindFunctionBody(functionCode);
            string functionBody = functionCode.Substring(bodyIndices.Item1, bodyIndices.Item2 - bodyIndices.Item1);
            string modifiedBody = ReadGlobalVariablesCode(table, details.PartitionKey, variables) + functionBody 
                + WriteGlobalVariablesCode(connectionString,details.Table, details.PartitionKey,variables);
            string finalCode = functionCode.Substring(0, bodyIndices.Item1) + modifiedBody +
                   functionCode.Substring(bodyIndices.Item2, functionCode.Length - bodyIndices.Item2) + _scalarCode;
            return finalCode;
        }

        public static void ProcessInitialState(ApiConfig config)
        {
            //Generate the assembly for code to get all the variables and their initial values
            Assembly assembly = GetInitialStateAssembly(config.State);
            Type stateType = assembly.GetType("State");
            MethodInfo getStateMethod = stateType.GetMethod("GetState");
            var stateList =
                (Collection<KeyValuePair<string, object>>)
                    getStateMethod.Invoke(null, BindingFlags.InvokeMethod, null, null, CultureInfo.CurrentCulture);

            //send the initial values to the database
            UpdateOrCreateState(config, stateList);
        }

        private static string GetInitializationCode(string initializations)
        {
            //All the neccesary wrapper code to get the initial values of the variables. 
            //todo: probably a less awkward way to obtain these values
            string code = @"
                using System.Collections.Generic;
                using System.Collections.ObjectModel;
                using System.Reflection;
                public static class State {{
                    {0}
                public static Collection<KeyValuePair<string, object>> GetState() {{ 
                    Collection<KeyValuePair<string,object>> variableLIst = new Collection<KeyValuePair<string,object>>();
                    foreach (FieldInfo field in typeof(State).GetFields()) {{
                        variableLIst.Add(new KeyValuePair<string, object>(field.Name, field.GetValue(null)));
                    }}
                    return variableLIst;
                 }}
            }}";
            return string.Format(code, initializations);
        }

        private static Assembly GetInitialStateAssembly(string initializers)
        {
            //generate an assembly using Roslyn
            using (var stream = new MemoryStream())
            {
                string assemblyFileName = "gen" + Guid.NewGuid().ToString().Replace("-", "") + ".dll";

                CSharpCompilation compilation = CSharpCompilation.Create(assemblyFileName,
                    new[] {CSharpSyntaxTree.ParseText(GetInitializationCode(initializers))},
                    new[]
                    {
                        MetadataReference.CreateFromFile(typeof (object).Assembly.Location)
                    },
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    );
                compilation.Emit(stream);
                Assembly assembly = Assembly.Load(stream.GetBuffer());
                return assembly;
            }
        }

        public static void UpdateOrCreateState(ApiConfig config, Collection<KeyValuePair<string, object>> variableList)
        {
            //Create a table object given the table storage information.
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
            CloudTableClient client = storageAccount.CreateCloudTableClient();
            CloudTable table = client.GetTableReference(config.TableStorage.Table);

            Type genericScalar = typeof(ScalarEntity<>);
            TableBatchOperation batchOperation = new TableBatchOperation();

            Dictionary<string,Type> variableNames = new Dictionary<string, Type>();
            foreach (var variable in variableList)
            {
                //checks if the variable is a "scalar" type, and if so, creates a ScalarEntity<T> and inserts it into Azure Storage
                Type type = variable.Value.GetType();
                variableNames.Add(variable.Key, type);
                if (type.IsPrimitive || type == typeof (Decimal) || type == typeof (string))
                {
                    var scalarType = genericScalar.MakeGenericType(type);
                    //must use dynamic as we don't know at runtime what type of ScalarEntity<T> the variable is
                    dynamic scalar = Activator.CreateInstance(scalarType);
                    scalar.PartitionKey = config.ApiName;
                    scalar.RowKey = variable.Key;
                    //must use dynamic, because cannot implicitly convert object to type T
                    dynamic value = Convert.ChangeType(variable.Value, type);
                    scalar.Value = value;
                    batchOperation.InsertOrReplace(scalar);
                }
            }
            table.ExecuteBatch(batchOperation);

            foreach (FunctionDetails func in config.Functions)
            {
                func.GlobalVariableTypes = variableNames;
            }
        }
    }
}
