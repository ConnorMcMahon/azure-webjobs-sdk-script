// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Text;
using YamlDotNet.Serialization;

namespace Microsoft.Azure.WebJobs.Script
{

    //Schema for the Yaml object
    public class ApiConfig
    {      
        public string Language { get; set; }
        public TableDetails TableStorage { get; set; }
        public string CommonCode { get; set; }
        //supressed to allow yaml parser to assign a value
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public Collection<FunctionDetails> Functions { get; set; }
    }

    public class TableDetails
    {
        public string Table { get; set; }
        public string PartitionKey { get; set; }
        public string Connection { get; set; }

    }

    public class FunctionDetails
    {
        private Collection<BindingDetail> _bindingDetails;
        public string Name { get; set; }
        public string Route { get; set; }
        public string Code { get; set; }
        //supressed to allow yaml parser to assign a value to the property
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage",
            "CA2227:CollectionPropertiesShouldBeReadOnly")]
        [YamlMember(Alias = "bindings")]
        public Collection<string> BindingStrings
        {
            get { return BindingStrings; }
            set
            {
                _bindingDetails = new Collection<BindingDetail>();
                foreach (string bindingString in value)
                {
                    string[] bindingParts = bindingString.Split(new char[] { ':', '-' });
                    var bindingDetail = new BindingDetail();
                    bindingDetail.Name = bindingParts[0];
                    bindingDetail.BindingType = bindingParts[1];
                    bindingDetail.Direction = bindingParts[2];
                    _bindingDetails.Add(bindingDetail);
                }
            }
        }
        public Collection<BindingDetail> BindingDetails
        {
            get { return _bindingDetails; }
        }
    }


    public class BindingDetail
    {
        public string Name { get; set; }
        public string BindingType { get; set; }
        public string Direction { get; set; }
    }
    
}


