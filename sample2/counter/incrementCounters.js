module.exports = function (context, req) {
    var counters = context.state.getDictionary("counters");
    counters.getValue(req.parameters.counterName, function(counterVal,err){
        if(err){
            context.res = {
                status: 400,
                body: "Error: The counter is not initialized."
            }
        } else {
            counterVal += req.parameters.addValue;
            counters.setValue(req.parameters.counterName, counterVal);
            context.res = {
                status:200,
                body: "Incremented " + req.parameters.counterName + " by " + req.parameters.addValue
            }
        }
        context.res.headers = { 'Content-Type': 'text/plain' };
        context.done();
    });   
}