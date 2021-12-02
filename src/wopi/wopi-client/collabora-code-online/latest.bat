:: https://www.collaboraoffice.com/code/docker-2

docker pull collabora/code:latest

docker rm collabora-code -f

docker run -t -d -p 0.0.0.0:9980:9980 -e "domain=host\\.docker\\.internal:44355" -e "username=admin" -e "password=S3cRet" --name=collabora-code --restart always --cap-add MKNOD collabora/code:latest

:: pull out the latest version of the main config and startup files so we can do a local diff to find changes

docker cp collabora-code:start-collabora-online.sh start-collabora-online.sh
docker cp collabora-code:etc/coolwsd/coolwsd.xml coolwsd.xml
docker cp collabora-code:etc/coolwsd/proof_key proof_key 
docker cp collabora-code:etc/coolwsd/proof_key.pub proof_key.pub

pause
