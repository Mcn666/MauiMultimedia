// JavaScript 测试样本
const app = {
  name: 'MauiMultimedia',
  version: '1.0.0',
  init() {
    console.log(`Starting ${this.name} v${this.version}`);
    document.querySelectorAll('.viewer-page').forEach(page => {
      page.addEventListener('click', this.handleClick);
    });
  },
  handleClick(e) {
    const target = e.currentTarget;
    console.log('Clicked:', target.id);
  }
};

app.init();
