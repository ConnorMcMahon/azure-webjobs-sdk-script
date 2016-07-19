module.exports = function (context, req) {
    context.state.getValue("i", function(i) {
        context.state.getValue("j", function (j) {
            i += 2;
            j -= 2;
            context.res = {
                status: 200,
                body: "i = " + i + "\tj = " + j
            };
            context.state.setValue("i", i);
            context.state.setValue("j", j);
            context.res.headers = {
                'Content-Type': 'text/plain'
            };
            context.done();
        });
    });
}