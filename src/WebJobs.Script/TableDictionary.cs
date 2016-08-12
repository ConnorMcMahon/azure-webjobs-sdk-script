using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Data.Edm.Library.Expressions;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Script
{
    [CLSCompliant(false)]
    public class TableDictionary<TKey, TValue> : IDictionary<TKey, TValue>
    {
        private IDictionary<TKey, TValue> _currentValues;
        private HashSet<TKey> _deletedKeys;
        private IDictionary<TKey, DynamicTableEntity> _storedValues; 
        private string _currentNestingPosition;
        private string _partitionKey;
        private CloudTable _table;
        private bool _retrievedAll;

        public TableDictionary(CloudTable table, string partitionKey, string currentNestingPosition)
        {
            _storedValues = new Dictionary<TKey, DynamicTableEntity>();
            _currentValues = new Dictionary<TKey, TValue>();
            _deletedKeys = new HashSet<TKey>();
            _partitionKey = partitionKey;
            _table = table;
            _currentNestingPosition = currentNestingPosition;
            _retrievedAll = false;
            IsReadOnly = false;
            IsFixedSize = false;
        }

        public bool ContainsKey(TKey key)
        {
            GrabValue(key);
            if (_currentValues.ContainsKey(key))
            {
                return true;
            }
            //todo: try reading it in otherwise
            return false;
        }

        public void Add(TKey key, TValue value)
        {
            _currentValues.Add(key, value);
        }

        public bool Remove(TKey key)
        {
            GrabValue(key);
            if (!_deletedKeys.Contains(key))
            {
                if (_currentValues.Remove(key))
                {
                    _deletedKeys.Add(key);
                    return true;
                }
                return false;
            }
            return false;
        }

        private static object ChangeType(DynamicTableEntity entity)
        {
            Type valueType = typeof (TValue);
            if (valueType == typeof(bool))
            {
                return entity.Properties["Value"].BooleanValue;
            } else if (valueType == typeof(string))
            {
                return entity.Properties["Value"].StringValue;
            } else if (valueType == typeof (int))
            {
                return entity.Properties["Value"].Int32Value;
            } else if (valueType == typeof (long))
            {
                return entity.Properties["Value"].Int64Value;
            } else if (valueType == typeof (DateTime))
            {
                return entity.Properties["Value"].DateTime;
            } else if (valueType == typeof (double))
            {
                return entity.Properties["Value"].DoubleValue;
            } else if (valueType == typeof (Guid))
            {
                return entity.Properties["Value"].GuidValue;
            }
            return null;
        }

        private void GrabValue(TKey key)
        {
            if (_retrievedAll || _storedValues.ContainsKey(key))
            {
                return;
            }
            string finalRowKey = GlobalStateUtility.EscapeAndTranslateKey(_currentNestingPosition, key.ToString());
            TableOperation retrieveOperation = TableOperation.Retrieve<DynamicTableEntity>(_partitionKey,finalRowKey);
            var result = _table.Execute(retrieveOperation);
            var tableEntity = result.Result as DynamicTableEntity;
            if (tableEntity != null)
            {
                _storedValues[key] = tableEntity;
                if (tableEntity.Properties["Value"] != null)
                {
                    _currentValues[key] = (TValue) ChangeType(tableEntity);
                }
                else
                {
                    if (typeof (IDictionary).IsAssignableFrom(typeof(TValue)))
                    {
                        Type[] arguments = typeof (TValue).GetGenericArguments();
                        var newTableType = typeof (TableDictionary<,>).MakeGenericType(arguments);
                        object[] args = new object[3];
                        args[0] = _table;
                        args[1] = _partitionKey;
                        args[2] = finalRowKey;
                        _currentValues[key] = (TValue) Activator.CreateInstance(newTableType, args);
                    }
                }
            }
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            return _currentValues.TryGetValue(key, out value);
        }

        public TValue this[TKey key]
        {
            get
            {
                GrabValue(key);
                return _currentValues[key];
            }
            set
            {
                GrabValue(key);
                _currentValues[key] = value;
            }
        }

        ICollection<TValue> IDictionary<TKey, TValue>.Values
        {
            get
            {
                GrabAllElements();
                return _currentValues.Values;
            }
        }
        ICollection<TKey> IDictionary<TKey, TValue>.Keys
        {
            get
            {
                GrabAllElements();
                return _currentValues.Keys;
            }
        }

        public bool Flush(string consistencyType)
        {
            //todo: support remove key operations
            TableBatchOperation batch = new TableBatchOperation();
            foreach (TKey key in _currentValues.Keys)
            {
                //use dynamic so the system can process it as an entity property
                dynamic currentValue = _currentValues[key];
                if (_storedValues.ContainsKey(key))
                {
                    var storedValue = (TValue) ChangeType(_storedValues[key]);
                    if (!currentValue.Equals(storedValue))
                    {
                        _storedValues[key].Properties["Value"] = new EntityProperty(currentValue);
                        _storedValues[key].ETag = consistencyType.Equals("optimistic") ? _storedValues[key].ETag : "*";
                        batch.Merge(_storedValues[key]);
                    }
                }
                else
                {
                    DynamicTableEntity newEntity = new DynamicTableEntity();
                    newEntity.PartitionKey = _partitionKey;
                    newEntity.RowKey = GlobalStateUtility.EscapeAndTranslateKey(_currentNestingPosition, key.ToString());
                    newEntity.Properties["Value"] = new EntityProperty(currentValue);
                    newEntity.Properties["type"] = new EntityProperty(currentValue.GetType().ToString());
                    batch.Insert(newEntity);
                }
            }
            foreach (TKey key in _deletedKeys)
            {
                if (_currentValues.ContainsKey(key))
                {
                    //the key was readded later on and was handled above.
                    _deletedKeys.Remove(key);
                }
                else if(_storedValues.ContainsKey(key))
                {
                    batch.Delete(_storedValues[key]);
                }
            }
           
            try
            {
                if (batch.Count > 0)
                {
                    _table.ExecuteBatch(batch);
                }    
                return true;
            }
            catch (StorageException ex)
            {
                if (ex.RequestInformation.HttpStatusCode == 412)
                {
                    //todo: log exception
                }
                return false;
            }
        }

        public void ClearCache()
        {
            _storedValues.Clear();
            _deletedKeys.Clear();
            _currentValues.Clear();
        }

        public bool IsReadOnly { get; }
        public bool IsFixedSize { get; }

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            Add(item.Key, item.Value);
        }

        public void Clear()
        {
            foreach (TKey key in _storedValues.Keys)
            {
                _deletedKeys.Add(key);
            }
            _storedValues.Clear();
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            throw new NotImplementedException();
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            GrabValue(item.Key);
            if (!_deletedKeys.Contains(item.Key))
            {
                if (_currentValues.Remove(item))
                {
                    _deletedKeys.Add(item.Key);
                    return true;
                }
                return false;
            }
            return false;
        }

        public int Count
        {
            get
            {
                GrabAllElements();
                return _currentValues.Count;
            }
        }

        private void GrabAllElements()
        {
            if (!_retrievedAll)
            {
                //grabs everything with the proper 
                TableQuery<DynamicTableEntity> query = new TableQuery<DynamicTableEntity>().Where(
                    TableQuery.CombineFilters(
                        TableQuery.CombineFilters(
                            TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.GreaterThan, _currentNestingPosition + "_"),
                            TableOperators.And,
                            TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.LessThan, _currentNestingPosition + "`" )
                        ),
                        TableOperators.And,
                        TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, _partitionKey)
                    )
                );
                bool result1 = (_currentNestingPosition + "_").CompareTo("counters_mainpage") < 0;
                bool result2 = (_currentNestingPosition + "`").CompareTo("counters_mainpage") > 0;
                Console.Write(result1);
                Console.Write(result2);
                var entities = _table.ExecuteQuery(query);
                foreach (DynamicTableEntity entity in entities)
                {
                    string key = GlobalStateUtility.ExtractLastVariable(entity.RowKey);
                    TKey keyValue = (TKey) Convert.ChangeType(key, typeof (TKey));
                    _storedValues[keyValue] = entity;

                        if (entity.Properties["type"] != null)
                        {
                            _currentValues[keyValue] = (TValue)ChangeType(entity);
                        }
                        else
                        {
                            if (typeof(IDictionary).IsAssignableFrom(typeof(TValue)))
                            {
                                Type[] arguments = typeof(TValue).GetGenericArguments();
                                var newTableType = typeof(TableDictionary<,>).MakeGenericType(arguments);
                                object[] args = new object[3];
                                args[0] = _table;
                                args[1] = _partitionKey;
                                args[2] = entity.RowKey;
                                _currentValues[keyValue] = (TValue)Activator.CreateInstance(newTableType, BindingFlags.CreateInstance, args);
                            }
                        }
                }

                _retrievedAll = true;
            }
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            GrabAllElements();
            return _currentValues.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _currentValues.GetEnumerator();
        }
    }
}
