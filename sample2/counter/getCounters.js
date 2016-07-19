module.exports = function (context, req) {
    var counters = context.state.getDictionary("counters");
    counters.getValue(req.parameters.counterName, function(counterVal, err){
        if (err) {
            context.res = {
                status: 400,
                body: err
            };
        } else {
            context.res = {
                status: 200,
                body: counterVal
            };
        }
        context.res.headers = { 'Content-Type': 'text/plain' };
        context.done();
    });   
}