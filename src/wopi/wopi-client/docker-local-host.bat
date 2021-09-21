rem run docker connected to the localhost network 

docker run -t -d -p 0.0.0.0:9980:9980 -e "domain=host\\.docker\\.internal:44355" -e "username=admin" -e "password=S3cRet" --name=collabora_online --restart always --cap-add MKNOD collabora/code

rem disable https 

docker cp loolwsd.xml collabora_online:/etc/loolwsd/loolwsd.xml