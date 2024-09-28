release: release-win-x64 release-linux-x64

release-win-x64:
	dotnet publish . \
		--configuration Release \
		--runtime win-x64 \
		-p:PublishSingleFile=true \
		-p:EnableCompressionInSingleFile=true \
		--self-contained

release-linux-x64:
	dotnet publish . \
		--configuration Release \
		--runtime linux-x64 \
		-p:PublishSingleFile=true \
		-p:EnableCompressionInSingleFile=true \
		--self-contained

.PHONY: release release-win-x64 release-linux-x64