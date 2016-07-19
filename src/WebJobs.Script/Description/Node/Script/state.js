var azure = require('azure-storage');
var entityGen = azure.TableUtilities.entityGenerator;

module.exports = State;

function State(accountName, accountKey, tableName, apiName, variableName, storageClient) {
    if(!variableName){
        variableName = "";
    }
    if(storageClient){
        this.storageClient = storageClient;
    } else {
        this.storageClient = azure.createTableService(accountName, accountKey);
    }
    this.variableName = variableName;
    this.apiName = apiName; 
    this.tableName = tableName;
    this.variables = {};
    this.dictionaries = {};
    this.changedVariables = {};
    this.storageClient.createTableIfNotExists(tableName, function tableCreated(error) {
        if(error) {
            throw error;
        }
    });
}


State.prototype = {
    generateRowKey: function(variableName){
        if(this.variableName === ""){
            return variableName;
        }
        return this.variableName + "_" + variableName;
    },
    getValue: function(variableName, callback){
        if(!this.variables[variableName]) {
            var newVariableName = this.generateRowKey(variableName);
            //todo: use apiname as partition key
            this.storageClient.retrieveEntity(this.tableName, this.apiName, newVariableName, function(err, entity){
                if(!err){
                    if(entity && entity.Value){
                        callback(entity.Value["_"], null);
                    } else {
                        callback(null, "the entity is not a value.");
                    }    
                } else {
                    callback(null, err);
                }
            });
        } else {
            return callback(this.variables[variableName], null);
        }
    },
    setValue: function(variableName, value) {
        this.changedVariables[variableName] = value;
        this.variables[variableName] = value;
    },
    getDictionary: function(dictionaryName) {
        if(!this.dictionaries[dictionaryName]){
            var newDictionaryName = this.generateRowKey(dictionaryName);
            //todo: use apiname as partition key
            this.dictionaries[dictionaryName] = new State(null, null, this.tableName, this.apiName, newDictionaryName, this.storageClient);
        }
        return this.dictionaries[dictionaryName];
    },
    addDictionary: function(dictionaryName, dictionary){
        if(!this.dictionaries[dictionaryName]){
            var newDictionaryName = this.generateRowKey(dictionaryName);
            var newDictionary = new State(null, null, this.tableName, this.apiName, newDictionaryName, this.storageClient)
            newDictionary.variables = dictionary;
            newDictionary.changedVariables = dictionary; 
            this.dictionaries[dictionaryName] = newDictionary;
        }
    },
    flush: function() {
        var self = this;
        for (variable in self.changedVariables){
            var variableName = self.generateRowKey(variable);
            console.log(variableName);
            var variableEntity =  {
                PartitionKey: entityGen.String(self.apiName),
                RowKey: entityGen.String(variableName),
                Value: self.changedVariables[variable]
            }
            self.storageClient.insertOrReplaceEntity(self.tableName, variableEntity, function() {});
        }
        self.changedVariables = {};
        for (dictionary in this.dictionaries){
            self.dictionaries[dictionary].flush();
        }
    }
}
