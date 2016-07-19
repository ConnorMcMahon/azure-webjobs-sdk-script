module.exports = function (context, req) {
    var counters = context.state.getDictionary("counters");
    counters.setValue(req.parameters.counterName, 0);   
    context.res = {
        status: 200,
        body: "Initialized " + req.parameters.counterName
    };
    context.done();
}