# ChatAIBox Linux 中转服务

这是独立的 Linux 消息中转程序，供自建服务器使用。两端主动连接 HTTPS/WSS，换网后重连；支持与客户端的 IPv6 P2P 直连模式切换。手机保留永久聊天库，服务仅在内存转发端到端加密分片，不保存聊天正文、离线队列或 API Key。

## 安装

适用于使用 systemd 的 Linux，需已安装 Node.js 22+、Nginx 和有效的 HTTPS 证书。不会创建云资源、修改安全组或购买服务。

```sh
tar -xzf ChatAIBox-0.3.3-Linux-relay.tar.gz
cd chataibox-relay
# 分发包已包含 ws 运行依赖；从源码安装时先执行：
npm ci --omit=dev --ignore-scripts
sudo sh install.sh
```

安装脚本使用 `/opt/chataibox-relay` 和 systemd 的临时服务身份，只监听本机 `127.0.0.1:39181`。首次安装会生成一个 64 位随机访问密钥，**仅显示一次，请私下保存**。`/etc/chataibox-relay.env` 只保存 SHA-256 哈希，权限 600；已有文件不会被覆盖。两端设置相同的服务地址，并填写这个“服务器访问密钥”。聊天配对密钥是另一份端到端加密凭据，不能用来代替它。

在已有 HTTPS Nginx 站点中添加：

```nginx
location = /chataibox/relay {
    proxy_pass http://127.0.0.1:39181/relay;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_set_header Authorization $http_authorization;
    proxy_read_timeout 90s;
    proxy_send_timeout 180s;
    proxy_buffering off;
    access_log off;
}
location = /chataibox/health {
    proxy_pass http://127.0.0.1:39181/health;
    access_log off;
}
```

执行 `sudo nginx -t`，通过后执行 `sudo systemctl reload nginx`。客户端服务地址填 `https://relay.example.com/chataibox`，将域名替换成自己的实际域名。不接受查询参数或 URL 中的密码；访问密钥经 HTTPS 的 Authorization 请求头提交，不进入 URL。不要在代理日志中记录该请求头。健康检查不需要密钥，只有 WebSocket 转发入口需要鉴权。

## 检查、更新与轮换

```sh
systemctl status chataibox-relay.service
curl --fail http://127.0.0.1:39181/health
```

缺失或无效的 `RELAY_ACCESS_KEY_HASH` 会使服务拒绝启动。缺失或错误的客户端访问密钥返回 HTTP 401，不能创建中转房间。不会自动降级为无鉴权服务。旧客户端没有访问密钥设置，必须先更新两端再启用受保护入口。

轮换密钥：在私有位置备份现有 `/etc/chataibox-relay.env`，使用 `sudo node generate-key.cjs /etc/chataibox-relay.next.env` 生成新的文件，将新文件安装到原配置路径，权限保持 600，重启服务并更新两端。重启会断开已有连接，但不会删除手机聊天。更换后旧密钥无法重新连接。

程序可由 `PORT` 调整本机监听端口，反向代理需同步修改。服务不在手机离线时缓存消息；电脑退出前需把未确认消息同步到手机。访问密钥控制服务器使用权限，不能隐藏连接时间和流量大小，也不能取代 HTTPS 或聊天配对加密。
