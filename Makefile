release:
	dotnet publish . \
		--configuration Release \
		--runtime win-x64 \
		-p:PublishSingleFile=true \
		-p:EnableCompressionInSingleFile=true \
		--self-contained

	dotnet publish . \
		--configuration Release \
		--runtime linux-x64 \
		-p:PublishSingleFile=true \
		-p:EnableCompressionInSingleFile=true \
		--self-contained

.PHONY: release