﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class FunctionMetadata
    {
        private readonly Collection<string> _globalVariables;

        public FunctionMetadata()
        {
            Bindings = new Collection<BindingMetadata>();
            _globalVariables = new Collection<string>();
        }

        public string Name { get; set; }

        /// <summary>
        /// The primary entry point for the function (to disambiguate if there are multiple
        /// scripts in the function directory).
        /// </summary>
        public string ScriptFile { get; set; }

        public Collection<string> GlobalVariables
        {
            get { return _globalVariables; }
        }

        public void RegisterVariables(Collection<string> variables)
        {
            if (variables != null)
            {
                foreach (string variable in variables)
                {
                    GlobalVariables.Add(variable);
                    //var metadata = new TableBindingMetadata();
                    //metadata.Name = variable;
                    //metadata.Direction = BindingDirection.InOut;
                    //metadata.Type = "table";
                    //metadata.TableName = TableDetails.Table;
                    //metadata.PartitionKey = TableDetails.PartitionKey;
                    //metadata.VariableName = variable;
                    //Bindings.Add(metadata);
                }
            }
        }

        public TableDetails TableDetails { get; set; }

        /// <summary>
        /// Gets or sets the optional named entry point for a function.
        /// </summary>
        public string EntryPoint { get; set; }

        public string ScriptCode { get; set; }

        public ScriptType ScriptType { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the function is disabled.
        /// <remarks>
        /// A disabled function is still compiled and loaded into the host, but it will not
        /// be triggered automatically, and is not publically addressable (except via admin invoke requests).
        /// </remarks>
        /// </summary>
        public bool IsDisabled { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the function is excluded.
        /// <remarks>
        /// An excluded function is completely skipped during function loading. It will not be compiled
        /// and will not be loaded into the host.
        /// </remarks>
        /// </summary>
        public bool IsExcluded { get; set; }

        public Collection<BindingMetadata> Bindings { get; private set; }

        public IEnumerable<BindingMetadata> InputBindings
        {
            get
            {
                return Bindings.Where(p => p.Direction != BindingDirection.Out);
            }
        }

        public IEnumerable<BindingMetadata> OutputBindings
        {
            get
            {
                return Bindings.Where(p => p.Direction != BindingDirection.In);
            }
        }
    }
}
