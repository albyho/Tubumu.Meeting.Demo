<template>
  <div id="app">
    <el-container>
      <el-header>Header</el-header>
      <el-main>Main</el-main>
    </el-container>
  </div>
</template>

<script>
import * as mediasoupClient from "mediasoup-client";
import * as signalR from "@microsoft/signalr";

export default {
  name: "app",
  components: {},
  data() {
    return {
      connection: null,
      device: null,
      sendTransport: null,
      recvTransport: null
    };
  },
  mounted() {
    this.run();
  },
  methods: {
    run() {
      try {
        const accessToken =
          "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1lIjoiMjkiLCJnIjoi5Yy76ZmiIiwibmJmIjoxNTg0MzQ5MDQ2LCJleHAiOjE1ODY5NDEwNDYsImlzcyI6Imlzc3VlciIsImF1ZCI6ImF1ZGllbmNlIn0._bGG1SOF9WqY8TIErRkxsh9_l_mFB_5JcGrKO1GyQ0E";
        this.connection = new signalR.HubConnectionBuilder()
          .withUrl(
            `http://localhost:5000/hubs/meetingHub?access_token=${accessToken}`
          )
          .withAutomaticReconnect({
            nextRetryDelayInMilliseconds: retryContext => {
              if (retryContext.elapsedMilliseconds < 60000) {
                // If we've been reconnecting for less than 60 seconds so far,
                // wait between 0 and 10 seconds before the next reconnect attempt.
                return Math.random() * 10000;
              } else {
                // If we've been reconnecting for more than 60 seconds so far, stop reconnecting.
                return null;
              }
            }
          })
          .build();
        this.connection.on("ReceiveMessage", data => {
          this.processMessage(data);
        });
        this.connection.start().catch(err => console.error(err));
      } catch (e) {
        console.log(e.message);
      }
    },
    processMessage(data) {
      console.log(data);
      if (data.code !== 200) {
        console.error(data.message);
        return;
      }
      // 连接成功
      if (data.internalCode === 10001) {
        this.connection
          .invoke("EnterRoom", "00000000-0000-0000-0000-000000000000")
          .catch(err => console.error(err));
      }
      // EnterRoom 成功
      if (data.internalCode === 10003) {
        this.connection
          .invoke("GetRouterRtpCapabilities")
          .catch(err => console.error(err));
      }
      // GetRouterRtpCapabilities 成功
      if (data.internalCode === 10005) {
        this.device = new mediasoupClient.Device();
        const rtpCapabilities = this.device.load({
          routerRtpCapabilities: data.data
        });
        this.connection
          .invoke("Join", rtpCapabilities)
          .catch(err => console.error(err));
      }
      // Join 成功
      if (data.internalCode === 10007) {
        this.connection
          .invoke("CreateWebRtcTransport", {
            forceTcp: false,
            producing: true,
            consuming: false,
            userData: { direction: "send" }
          })
          .catch(err => console.error(err));
        this.connection
          .invoke("CreateWebRtcTransport", {
            forceTcp: false,
            producing: false,
            consuming: true,
            userData: { direction: "recv" }
          })
          .catch(err => console.error(err));
      }
      // CreateWebRtcTransport 成功
      if (data.internalCode === 10009) {
        if (!data.data.userData || typeof data.data.userData !== "object")
          throw new TypeError("appData must be an object");

        const { id, iceParameters, iceCandidates, dtlsParameters } = data.data;

        if (data.data.userData.direction === "send") {
          this.sendTransport = this._mediasoupDevice.createSendTransport({
            id,
            iceParameters,
            iceCandidates,
            dtlsParameters
            // 还可添加 iceServers 等参数
          });

          this.sendTransport.on(
            "connect",
            (
              { dtlsParameters },
              callback,
              errback // eslint-disable-line no-shadow
            ) => {
              this.connection
                .invoke("ConnectWebRtcTransport", {
                  transportId: this.sendTransport.id,
                  dtlsParameters
                })
                .then(callback)
                .catch(errback);
            }
          );

          this.sendTransport.on(
            "produce",
            ({ kind, rtpParameters, appData }, callback, errback) => {
              try {
                // eslint-disable-next-line no-shadow
                this.connection.invoke("Produce", {
                  transportId: this.sendTransport.id,
                  kind,
                  rtpParameters,
                  appData
                });
                callback({ id });
              } catch (error) {
                errback(error);
              }
            }
          );
        } else {
          this.recvTransport = this._mediasoupDevice.createRecvTransport({
            id,
            iceParameters,
            iceCandidates,
            dtlsParameters
            // 还可添加 iceServers 等参数
          });

          this.recvTransport.on(
            "connect",
            (
              { dtlsParameters },
              callback,
              errback // eslint-disable-line no-shadow
            ) => {
              this.connection
                .invoke("ConnectWebRtcTransport", {
                  transportId: this.recvTransport.id,
                  dtlsParameters
                })
                .then(callback)
                .catch(errback);
            }
          );
        }
      }

      if (data.internalCode === 10011) {
        //
      }
    }
  }
};
</script>

<style>
body {
  margin: 0;
  background-color: #313131;
  color: #fff;
}

#app {
  font-family: "Avenir", Helvetica, Arial, sans-serif;
  -webkit-font-smoothing: antialiased;
  -moz-osx-font-smoothing: grayscale;
}

#app > .el-container {
  margin-bottom: 40px;
}

.el-header,
.el-footer {
  line-height: 60px;
}

.el-main {
  line-height: 160px;
}
</style>
