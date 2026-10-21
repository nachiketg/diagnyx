package diagnyx_test

import (
	"context"
	"log"

	diagnyx "github.com/nachiketg/diagnyx/packages/go"
)

func Example() {
	logger, err := diagnyx.NewLogger("my-api")
	if err != nil {
		log.Fatal(err)
	}

	logger.Info("App started")
	logger.Warn("Cache miss", diagnyx.Fields{"key": "user:42", "ttl": 300})
	logger.Error("Payment failed", diagnyx.Fields{"orderId": "ord-9921", "amount": 49.99})
}

func ExampleWithTraceContext() {
	logger, err := diagnyx.NewLogger("my-api")
	if err != nil {
		log.Fatal(err)
	}

	// Usually done once per request, e.g. in HTTP middleware.
	ctx := diagnyx.WithTraceContext(context.Background(),
		"4bf92f3577b34da6a3ce929d0e0e4736", "00f067aa0ba902b7")

	logger.InfoContext(ctx, "processing order", diagnyx.Fields{"orderId": "ord-9921"})
}

func ExampleWithBinaryPath() {
	logger, err := diagnyx.NewLogger("my-api", diagnyx.WithBinaryPath("/opt/diagnyx/diagnyx"))
	if err != nil {
		log.Fatal(err)
	}

	logger.Info("App started")
}

func ExampleDownload() {
	// Fetch the binary ahead of time, e.g. in a Docker build step, so the
	// first log call doesn't wait on the network.
	path, err := diagnyx.Download(context.Background())
	if err != nil {
		log.Fatal(err)
	}
	log.Println("diagnyx binary at", path)
}
